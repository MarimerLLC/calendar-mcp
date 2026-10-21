using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Core.Utilities;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using IcalCalendar = Ical.Net.Calendar;
using IcsCalendarEvent = Ical.Net.CalendarComponents.CalendarEvent;
using ModelCalendarEvent = CalendarMcp.Core.Models.CalendarEvent;

namespace CalendarMcp.Core.Providers.Dav;

/// <summary>
/// One VEVENT, or one occurrence of a recurring VEVENT, from a calendar object resource.
/// <see cref="RecurrenceKey"/> identifies the occurrence within its series and is null for
/// non-recurring events.
/// </summary>
public readonly record struct ExpandedEvent(IcsCalendarEvent Event, Occurrence? Occurrence, string? RecurrenceKey);

/// <summary>
/// Maps between Ical.Net VEVENT components and Adjutant's <see cref="ModelCalendarEvent"/>.
/// </summary>
public static partial class IcalMapper
{
    /// <summary>
    /// Most occurrences one recurring event may expand to in a single query. Ical.Net 4.3.1
    /// materializes every occurrence in the range, so a rule such as FREQ=MINUTELY over a
    /// broad range is rejected before expansion rather than exhausting memory.
    /// </summary>
    internal const int MaxOccurrencesPerEvent = 10_000;

    private const string UtcKeyFormat = "yyyyMMdd'T'HHmmss'Z'";
    private const string DateKeyFormat = "yyyyMMdd";

    public static ModelCalendarEvent ToCalendarEvent(
        IcsCalendarEvent icsEvent,
        string accountId,
        string calendarId,
        string eventId,
        Occurrence? occurrence = null,
        string? ownerEmail = null)
    {
        DateTimeOffset evtStart, evtEnd;
        DateOnly? startDate = null, endDate = null;

        if (icsEvent.IsAllDay)
        {
            // All-day (VALUE=DATE) events are floating dates: keep the date as written rather
            // than anchoring it to an instant, which would shift it in zones behind UTC.
            var (s, e) = IcsProviderService.GetAllDayDates(icsEvent, occurrence);
            startDate = s;
            endDate = e;
            evtStart = TimeZoneHelper.UtcMidnight(s);
            evtEnd = TimeZoneHelper.UtcMidnight(e);
        }
        else if (occurrence is not null)
        {
            evtStart = new DateTimeOffset(occurrence.Period.StartTime.AsUtc, TimeSpan.Zero);
            var occEnd = occurrence.Period.EndTime?.AsUtc
                         ?? occurrence.Period.StartTime.AsUtc
                         + (icsEvent.DtEnd?.AsUtc - icsEvent.DtStart?.AsUtc ?? TimeSpan.FromHours(1));
            evtEnd = new DateTimeOffset(occEnd, TimeSpan.Zero);
        }
        else
        {
            evtStart = icsEvent.DtStart != null
                ? new DateTimeOffset(icsEvent.DtStart.AsUtc, TimeSpan.Zero)
                : DateTimeOffset.MinValue;
            evtEnd = icsEvent.DtEnd != null
                ? new DateTimeOffset(icsEvent.DtEnd.AsUtc, TimeSpan.Zero)
                : evtStart;
        }

        var organizer = ExtractMailto(icsEvent.Organizer?.Value);
        var attendees = new List<string>();
        var attendeeDetails = new List<EventAttendee>();
        foreach (var att in icsEvent.Attendees)
        {
            var email = ExtractMailto(att.Value);
            if (string.IsNullOrEmpty(email))
                continue;
            attendees.Add(email);
            attendeeDetails.Add(new EventAttendee
            {
                Email = email,
                Name = att.CommonName ?? string.Empty,
                ResponseStatus = MapPartStat(att.ParticipationStatus),
                Type = att.Role?.ToUpperInvariant() switch
                {
                    "REQ-PARTICIPANT" => "required",
                    "OPT-PARTICIPANT" => "optional",
                    "NON-PARTICIPANT" => "resource",
                    _ => "required"
                },
                IsOrganizer = organizer.Length > 0 && email.Equals(organizer, StringComparison.OrdinalIgnoreCase)
            });
        }

        return new ModelCalendarEvent
        {
            Id = eventId,
            AccountId = accountId,
            CalendarId = calendarId,
            Subject = icsEvent.Summary ?? string.Empty,
            Start = evtStart,
            End = evtEnd,
            StartDate = startDate,
            EndDate = endDate,
            Location = icsEvent.Location ?? string.Empty,
            Body = icsEvent.Description ?? string.Empty,
            BodyFormat = "text",
            Organizer = organizer,
            OrganizerName = icsEvent.Organizer?.CommonName ?? string.Empty,
            Attendees = attendees,
            AttendeeDetails = attendeeDetails,
            ResponseStatus = GetSelfResponseStatus(icsEvent, ownerEmail),
            IsAllDay = icsEvent.IsAllDay,
            ShowAs = icsEvent.Transparency == TransparencyType.Transparent ? "free" : "busy",
            Sensitivity = icsEvent.Class?.ToUpperInvariant() switch
            {
                "PRIVATE" => "private",
                "CONFIDENTIAL" => "confidential",
                _ => "normal"
            },
            IsCancelled = icsEvent.Status?.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) == true,
            IsRecurring = IsRecurring(icsEvent) || icsEvent.RecurrenceId is not null,
            RecurrencePattern = icsEvent.RecurrenceRules?.FirstOrDefault()?.ToString(),
            Categories = icsEvent.Categories?.ToList() ?? [],
            CreatedDateTime = icsEvent.Created?.AsUtc,
            LastModifiedDateTime = icsEvent.LastModified?.AsUtc
        };
    }

    /// <summary>
    /// The account owner's own response: the PARTSTAT on their ATTENDEE entry, or accepted when
    /// they organized the event or it is a personal appointment with no attendees or organizer.
    /// </summary>
    internal static string GetSelfResponseStatus(IcsCalendarEvent icsEvent, string? ownerEmail)
    {
        var organizer = ExtractMailto(icsEvent.Organizer?.Value);
        if (!string.IsNullOrWhiteSpace(ownerEmail))
        {
            if (organizer.Equals(ownerEmail, StringComparison.OrdinalIgnoreCase))
                return "accepted";

            var self = icsEvent.Attendees.FirstOrDefault(a =>
                ExtractMailto(a.Value).Equals(ownerEmail, StringComparison.OrdinalIgnoreCase));
            if (self is not null)
                return MapPartStat(self.ParticipationStatus);
        }

        return icsEvent.Attendees.Count == 0 && organizer.Length == 0 ? "accepted" : "notResponded";
    }

    /// <summary>
    /// Expands a calendar object resource into events and occurrences that may overlap
    /// [<paramref name="windowStartUtc"/>, <paramref name="windowEndUtc"/>). Recurring events are
    /// expanded with slack, so callers filter the mapped events by their UTC range. Overridden
    /// occurrences (VEVENTs with RECURRENCE-ID) replace the master's generated occurrence.
    /// </summary>
    public static IEnumerable<ExpandedEvent> Expand(
        IcalCalendar calendar, DateTime windowStartUtc, DateTime windowEndUtc,
        CancellationToken cancellationToken = default)
    {
        var overrideKeys = calendar.Events
            .Where(e => e.RecurrenceId is not null)
            .Select(e => (e.Uid, Key: RecurrenceKey(e.RecurrenceId)))
            .ToHashSet();

        foreach (var evt in calendar.Events)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (evt.RecurrenceId is not null)
            {
                yield return new ExpandedEvent(evt, null, RecurrenceKey(evt.RecurrenceId));
            }
            else if (IsRecurring(evt))
            {
                // Ical.Net 4.3.1 selects occurrences by wall-clock time in the event's own zone and
                // ignores the bounds' zone, which can be off by up to 14h, so expand with a day of
                // slack (plus the event's length, to catch occurrences that start before the window).
                var length = evt.DtStart is not null && evt.DtEnd is not null
                    ? evt.DtEnd.AsUtc - evt.DtStart.AsUtc
                    : TimeSpan.Zero;
                var from = windowStartUtc.AddDays(-1) - (length > TimeSpan.Zero ? length : TimeSpan.Zero);
                var to = windowEndUtc.AddDays(1);
                EnsureExpansionIsBounded(evt, from, to);

                foreach (var occurrence in evt.GetOccurrences(new CalDateTime(from, "UTC"), new CalDateTime(to, "UTC")))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var key = RecurrenceKey(occurrence.Period.StartTime);
                    if (!overrideKeys.Contains((evt.Uid, key)))
                        yield return new ExpandedEvent(evt, occurrence, key);
                }
            }
            else
            {
                yield return new ExpandedEvent(evt, null, null);
            }
        }
    }

    /// <summary>
    /// Finds one occurrence of a recurring event by its <see cref="ExpandedEvent.RecurrenceKey"/>:
    /// the override VEVENT when there is one, otherwise the master's generated occurrence.
    /// Returns null when the series has no such occurrence (e.g. it was excluded).
    /// </summary>
    public static ExpandedEvent? FindOccurrence(IcalCalendar calendar, string recurrenceKey)
    {
        if (!TryParseRecurrenceKey(recurrenceKey, out var instant, out _))
            return null;

        foreach (var expanded in Expand(calendar, instant, instant.AddDays(1)))
        {
            if (expanded.RecurrenceKey == recurrenceKey)
                return expanded;
        }
        return null;
    }

    /// <summary>
    /// Removes one occurrence from a recurring calendar object: adds an EXDATE to the master and
    /// drops any override for that occurrence. Returns null when no VEVENT is left, in which case
    /// the caller deletes the resource.
    /// </summary>
    public static string? ExcludeOccurrence(string existingIcs, string recurrenceKey)
    {
        if (!TryParseRecurrenceKey(recurrenceKey, out var instant, out var isDate))
            throw new ProviderOperationException("The event id names an occurrence that is not valid.");

        var calendar = IcalCalendar.Load(existingIcs)
            ?? throw new InvalidOperationException("Failed to parse existing calendar data.");

        foreach (var overridden in calendar.Events
                     .Where(e => e.RecurrenceId is not null && RecurrenceKey(e.RecurrenceId) == recurrenceKey)
                     .ToList())
        {
            calendar.Events.Remove(overridden);
        }

        var master = calendar.Events.FirstOrDefault(e => e.RecurrenceId is null && IsRecurring(e));
        if (master is not null)
        {
            var excluded = ToExceptionDate(master.DtStart, instant, isDate);
            // The TZID parameter is serialized from the list, not from its dates.
            var exceptionDates = new PeriodList { TzId = excluded.TzId };
            exceptionDates.Add(excluded);
            master.ExceptionDates.Add(exceptionDates);
            master.LastModified = new CalDateTime(DateTime.UtcNow);
        }

        return calendar.Events.Count == 0
            ? null
            : Serialize(calendar);
    }

    /// <summary>
    /// An EXDATE in the same form as the series' DTSTART (date, UTC, zoned or floating).
    /// Ical.Net 4.3.1 only excludes an occurrence whose EXDATE is written in the DTSTART's zone.
    /// </summary>
    private static IDateTime ToExceptionDate(IDateTime? seriesStart, DateTime instantUtc, bool isDate)
    {
        if (isDate)
            return new CalDateTime(instantUtc.Year, instantUtc.Month, instantUtc.Day) { HasTime = false };
        if (seriesStart is null || seriesStart.IsUtc)
            return new CalDateTime(instantUtc, "UTC");

        var tzId = seriesStart.TzId;
        if (string.IsNullOrWhiteSpace(tzId))
            return new CalDateTime(DateTime.SpecifyKind(instantUtc.ToLocalTime(), DateTimeKind.Unspecified));

        var zone = TimeZoneHelper.TryGetTimeZone(tzId);
        return zone is null
            ? new CalDateTime(instantUtc, "UTC")
            : new CalDateTime(TimeZoneInfo.ConvertTimeFromUtc(instantUtc, zone), tzId);
    }

    /// <summary>
    /// Identifies one occurrence in its series: the UTC start instant, or the date for all-day
    /// series. Matches the RECURRENCE-ID an override would carry for that occurrence.
    /// </summary>
    public static string RecurrenceKey(IDateTime start) =>
        start.HasTime
            ? start.AsUtc.ToString(UtcKeyFormat, CultureInfo.InvariantCulture)
            : start.Value.ToString(DateKeyFormat, CultureInfo.InvariantCulture);

    internal static bool TryParseRecurrenceKey(string key, out DateTime instantUtc, out bool isDate)
    {
        isDate = key.Length == DateKeyFormat.Length;
        var ok = DateTime.TryParseExact(key, isDate ? DateKeyFormat : UtcKeyFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out instantUtc);
        instantUtc = DateTime.SpecifyKind(instantUtc, DateTimeKind.Utc);
        return ok;
    }

    private static bool IsRecurring(IcsCalendarEvent evt) =>
        evt.RecurrenceRules?.Count > 0 || evt.RecurrenceDates?.Count > 0;

    /// <summary>
    /// Throws when a recurring event could expand to more than <see cref="MaxOccurrencesPerEvent"/>
    /// occurrences between <paramref name="from"/> and <paramref name="to"/>. The estimate is an
    /// upper bound: each BY* list is assumed to multiply the occurrences in a period.
    /// </summary>
    private static void EnsureExpansionIsBounded(IcsCalendarEvent evt, DateTime from, DateTime to)
    {
        if (evt.DtStart is not null && evt.DtStart.AsUtc > from)
            from = evt.DtStart.AsUtc;

        double estimate = evt.RecurrenceDates?.Sum(p => p.Count) ?? 0;
        foreach (var rule in evt.RecurrenceRules ?? [])
        {
            var ruleEnd = rule.Until != DateTime.MinValue && rule.Until < to ? rule.Until : to;
            if (ruleEnd <= from)
                continue;

            var step = rule.Frequency switch
            {
                FrequencyType.Secondly => TimeSpan.FromSeconds(1),
                FrequencyType.Minutely => TimeSpan.FromMinutes(1),
                FrequencyType.Hourly => TimeSpan.FromHours(1),
                FrequencyType.Weekly => TimeSpan.FromDays(7),
                FrequencyType.Monthly => TimeSpan.FromDays(28),
                FrequencyType.Yearly => TimeSpan.FromDays(365),
                _ => TimeSpan.FromDays(1)
            } * Math.Max(1, rule.Interval);

            double perPeriod = 1;
            foreach (var count in new[] { rule.BySecond.Count, rule.ByMinute.Count, rule.ByHour.Count, rule.ByDay.Count,
                         rule.ByMonthDay.Count, rule.ByYearDay.Count, rule.ByWeekNo.Count, rule.ByMonth.Count })
            {
                perPeriod *= Math.Max(1, count);
            }

            var ruleEstimate = (Math.Ceiling((ruleEnd - from) / step) + 1) * perPeriod;
            estimate += rule.Count > 0 ? Math.Min(ruleEstimate, rule.Count) : ruleEstimate;
        }

        if (estimate > MaxOccurrencesPerEvent)
        {
            throw new ProviderOperationException(
                $"A recurring event in this calendar repeats too often to list over this date range " +
                $"(more than {MaxOccurrencesPerEvent:N0} occurrences); narrow startDate and endDate.");
        }
    }

    public static string BuildNewEventIcs(
        string uid,
        string subject,
        DateTime start,
        DateTime end,
        string? location,
        IEnumerable<string>? attendees,
        string? body,
        string? timeZone,
        bool isAllDay = false)
    {
        var calendar = new IcalCalendar();
        var evt = new IcsCalendarEvent
        {
            Uid = uid,
            Summary = subject,
            Location = location,
            Description = body,
            DtStamp = new CalDateTime(DateTime.UtcNow)
        };

        if (isAllDay)
        {
            evt.DtStart = new CalDateTime(start.Date);
            evt.DtEnd = new CalDateTime(end.Date);
            evt.IsAllDay = true;
        }
        else if (!string.IsNullOrWhiteSpace(timeZone))
        {
            evt.DtStart = new CalDateTime(start, timeZone);
            evt.DtEnd = new CalDateTime(end, timeZone);
        }
        else
        {
            evt.DtStart = new CalDateTime(DateTime.SpecifyKind(start, DateTimeKind.Utc));
            evt.DtEnd = new CalDateTime(DateTime.SpecifyKind(end, DateTimeKind.Utc));
        }

        if (attendees is not null)
        {
            foreach (var email in attendees.Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                evt.Attendees.Add(new Attendee($"mailto:{email.Trim()}")
                {
                    Role = "REQ-PARTICIPANT",
                    ParticipationStatus = "NEEDS-ACTION",
                    Rsvp = true
                });
            }
        }

        calendar.Events.Add(evt);
        AddMissingTimeZones(calendar, evt);
        return Serialize(calendar);
    }

    public static string ApplyUpdates(
        string existingIcs,
        string? subject,
        DateTime? start,
        DateTime? end,
        string? location,
        List<string>? attendees,
        string? timeZone,
        bool? isAllDay = null)
    {
        var calendar = IcalCalendar.Load(existingIcs)
            ?? throw new InvalidOperationException("Failed to parse existing calendar data.");
        var evt = GetPrimaryEvent(calendar)
            ?? throw new InvalidOperationException("Existing calendar object has no VEVENT.");

        if (subject is not null)
            evt.Summary = subject;
        if (location is not null)
            evt.Location = location;
        var allDay = isAllDay ?? evt.IsAllDay;
        if (start is not null || isAllDay is not null)
        {
            var value = start ?? evt.DtStart?.Value;
            if (value is not null)
                evt.DtStart = ToEventDateTime(value.Value, allDay, timeZone ?? evt.DtStart?.TzId);
        }
        if (end is not null || isAllDay is not null)
        {
            var value = end ?? evt.DtEnd?.Value;
            if (value is not null)
                evt.DtEnd = ToEventDateTime(value.Value, allDay, timeZone ?? evt.DtEnd?.TzId);
        }
        evt.IsAllDay = allDay;
        if (attendees is not null)
        {
            evt.Attendees.Clear();
            foreach (var email in attendees.Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                evt.Attendees.Add(new Attendee($"mailto:{email.Trim()}")
                {
                    Role = "REQ-PARTICIPANT",
                    ParticipationStatus = "NEEDS-ACTION",
                    Rsvp = true
                });
            }
        }

        evt.LastModified = new CalDateTime(DateTime.UtcNow);
        AddMissingTimeZones(calendar, evt);
        return Serialize(calendar);
    }

    private static CalDateTime ToEventDateTime(DateTime value, bool isAllDay, string? timeZone)
    {
        if (isAllDay)
            return new CalDateTime(value.Date) { HasTime = false };
        return string.IsNullOrWhiteSpace(timeZone)
            ? new CalDateTime(DateTime.SpecifyKind(value, DateTimeKind.Utc))
            : new CalDateTime(value, timeZone);
    }

    /// <summary>
    /// CalDAV servers require a VTIMEZONE for every TZID a calendar object references
    /// (RFC 4791 §4.1), so add one for any zone the event uses that the object lacks.
    /// </summary>
    private static void AddMissingTimeZones(IcalCalendar calendar, IcsCalendarEvent evt)
    {
        if (evt.IsAllDay)
            return;

        foreach (var dt in new[] { evt.DtStart, evt.DtEnd })
        {
            var tzId = dt?.TzId;
            if (dt is null || string.IsNullOrWhiteSpace(tzId) || dt.IsUtc
                || calendar.TimeZones.Any(t => string.Equals(t.TzId, tzId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            VTimeZone vtz;
            try
            {
                vtz = VTimeZone.FromDateTimeZone(tzId, dt.Value.AddYears(-1), includeHistoricalData: false);
            }
            catch (Exception ex)
            {
                throw new ProviderOperationException(
                    $"timeZone \"{ProviderErrorFormatter.Sanitize(tzId)}\" is not a recognized time zone; " +
                    "use an IANA name such as America/Chicago.", ex);
            }
            calendar.AddTimeZone(vtz);
        }
    }

    public static string ApplyPartStat(string existingIcs, string attendeeEmail, string response)
    {
        var calendar = IcalCalendar.Load(existingIcs)
            ?? throw new InvalidOperationException("Failed to parse existing calendar data.");
        var evt = GetPrimaryEvent(calendar)
            ?? throw new InvalidOperationException("Existing calendar object has no VEVENT.");

        var partStat = response.ToLowerInvariant() switch
        {
            "accept" or "accepted" => "ACCEPTED",
            "tentative" or "tentativelyaccepted" => "TENTATIVE",
            "decline" or "declined" => "DECLINED",
            _ => throw new ArgumentException($"Unsupported response '{response}'.", nameof(response))
        };

        var attendee = evt.Attendees.FirstOrDefault(a =>
            string.Equals(ExtractMailto(a.Value), attendeeEmail, StringComparison.OrdinalIgnoreCase));
        if (attendee is null)
        {
            attendee = new Attendee($"mailto:{attendeeEmail}")
            {
                Role = "REQ-PARTICIPANT",
                Rsvp = true
            };
            evt.Attendees.Add(attendee);
        }

        attendee.ParticipationStatus = partStat;
        evt.LastModified = new CalDateTime(DateTime.UtcNow);
        return Serialize(calendar);
    }

    /// <summary>
    /// Serializes a calendar for a PUT. Ical.Net 4.3.1 writes every date-valued EXDATE/RDATE as
    /// a PERIOD ("20261006/P1D"), which RFC 5545 doesn't allow for EXDATE and servers may reject
    /// or misread, so those lines are rewritten as plain VALUE=DATE lists.
    /// </summary>
    internal static string Serialize(IcalCalendar calendar)
    {
        var ics = new CalendarSerializer().SerializeToString(calendar) ?? string.Empty;
        return DateListLine().Replace(ics, match =>
        {
            var line = match.Value.Replace("\r\n ", "").Replace("\r\n\t", "");
            var colon = line.IndexOf(':');
            var values = line[(colon + 1)..].Split(',');
            if (!values.All(v => DatePeriodValue().IsMatch(v)))
                return match.Value;

            var parameters = line[..colon].Split(';')
                .Where(p => !p.StartsWith("TZID=", StringComparison.OrdinalIgnoreCase)
                            && !p.StartsWith("VALUE=", StringComparison.OrdinalIgnoreCase));
            var fixedLine = string.Join(";", parameters) + ";VALUE=DATE:" + string.Join(",", values.Select(v => v[..8]));
            return Fold(fixedLine);
        });
    }

    /// <summary>
    /// Folds a content line at 75 octets (RFC 5545 §3.1). The lines rewritten here are ASCII.
    /// </summary>
    private static string Fold(string line)
    {
        if (line.Length <= 75)
            return line;

        var sb = new StringBuilder(line[..75]);
        for (var i = 75; i < line.Length; i += 74)
            sb.Append("\r\n ").Append(line[i..Math.Min(i + 74, line.Length)]);
        return sb.ToString();
    }

    /// <summary>An EXDATE or RDATE content line, including folded continuation lines.</summary>
    [GeneratedRegex(@"^(?:EXDATE|RDATE)[;:](?:[^\r\n]|\r\n[ \t])*", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DateListLine();

    /// <summary>A date value, optionally serialized as a period: 20261006 or 20261006/P1D.</summary>
    [GeneratedRegex(@"^\d{8}(?:/P[0-9A-Z]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DatePeriodValue();

    public static IcalCalendar? LoadCalendar(string? ics) =>
        string.IsNullOrWhiteSpace(ics) ? null : IcalCalendar.Load(ics);

    /// <summary>
    /// The VEVENT that represents the whole resource: the series master when the object also
    /// holds overridden occurrences.
    /// </summary>
    public static IcsCalendarEvent? GetPrimaryEvent(IcalCalendar calendar) =>
        calendar.Events.FirstOrDefault(e => e.RecurrenceId is null) ?? calendar.Events.FirstOrDefault();

    private static string MapPartStat(string? partStat) => partStat?.ToUpperInvariant() switch
    {
        "ACCEPTED" => "accepted",
        "TENTATIVE" => "tentative",
        "DECLINED" => "declined",
        "NEEDS-ACTION" => "notResponded",
        _ => "notResponded"
    };

    private static string ExtractMailto(Uri? uri)
    {
        if (uri is null)
            return string.Empty;

        if (uri.Scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase))
        {
            var s = uri.OriginalString;
            var idx = s.IndexOf(':');
            return idx >= 0 ? Uri.UnescapeDataString(s[(idx + 1)..]) : uri.AbsolutePath;
        }

        return uri.Authority ?? uri.ToString();
    }
}

/// <summary>
/// Minimal vCard 3.0/4.0 mapper covering fields already on <see cref="Contact"/>.
/// </summary>
public static partial class VCardMapper
{
    public static Contact ToContact(string vcard, string accountId, string contactId, string? etag)
    {
        var lines = Unfold(vcard);
        var emails = new List<ContactEmail>();
        var phones = new List<ContactPhone>();
        var addresses = new List<ContactAddress>();
        string displayName = "", given = "", surname = "", job = "", company = "", notes = "";
        DateTime? birthday = null;

        foreach (var line in lines)
        {
            var (name, paramsPart, value) = SplitLine(line);
            // Grouped properties (item1.EMAIL, item1.TEL, ...) carry the group as a prefix.
            var upper = name.Split('.').Last().ToUpperInvariant();

            switch (upper)
            {
                case "FN":
                    displayName = Unescape(value);
                    break;
                case "N":
                    var parts = SplitStructured(value);
                    surname = Unescape(parts.ElementAtOrDefault(0) ?? "");
                    given = Unescape(parts.ElementAtOrDefault(1) ?? "");
                    break;
                case "EMAIL":
                    emails.Add(new ContactEmail
                    {
                        Address = Unescape(value),
                        Label = TypeFromParams(paramsPart) ?? "other"
                    });
                    break;
                case "TEL":
                    phones.Add(new ContactPhone
                    {
                        Number = Unescape(value),
                        Label = TypeFromParams(paramsPart) ?? "other"
                    });
                    break;
                case "TITLE":
                    job = Unescape(value);
                    break;
                case "ORG":
                    company = Unescape(SplitStructured(value)[0]);
                    break;
                case "NOTE":
                    notes = Unescape(value);
                    break;
                case "BDAY":
                    if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var bd)
                        || DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out bd))
                        birthday = bd.Date;
                    break;
                case "ADR":
                    var adr = SplitStructured(value);
                    addresses.Add(new ContactAddress
                    {
                        Street = Unescape(adr.ElementAtOrDefault(2) ?? ""),
                        City = Unescape(adr.ElementAtOrDefault(3) ?? ""),
                        State = Unescape(adr.ElementAtOrDefault(4) ?? ""),
                        PostalCode = Unescape(adr.ElementAtOrDefault(5) ?? ""),
                        Country = Unescape(adr.ElementAtOrDefault(6) ?? ""),
                        Label = TypeFromParams(paramsPart) ?? "other"
                    });
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = $"{given} {surname}".Trim();

        return new Contact
        {
            Id = contactId,
            AccountId = accountId,
            DisplayName = displayName,
            GivenName = given,
            Surname = surname,
            EmailAddresses = emails,
            PhoneNumbers = phones,
            JobTitle = job,
            CompanyName = company,
            Notes = notes,
            Addresses = addresses,
            Birthday = birthday,
            Etag = etag
        };
    }

    public static string ToVCard(
        string uid,
        string displayName,
        string? givenName,
        string? surname,
        IEnumerable<string>? emails,
        IEnumerable<string>? phones,
        string? jobTitle,
        string? companyName,
        string? notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCARD");
        sb.AppendLine("VERSION:3.0");
        sb.AppendLine($"UID:{Escape(uid)}");
        sb.AppendLine($"FN:{Escape(displayName)}");
        sb.AppendLine($"N:{Escape(surname ?? "")};{Escape(givenName ?? "")};;;");
        if (emails is not null)
        {
            foreach (var email in emails.Where(e => !string.IsNullOrWhiteSpace(e)))
                sb.AppendLine($"EMAIL;TYPE=INTERNET:{Escape(email.Trim())}");
        }
        if (phones is not null)
        {
            foreach (var phone in phones.Where(p => !string.IsNullOrWhiteSpace(p)))
                sb.AppendLine($"TEL;TYPE=VOICE:{Escape(phone.Trim())}");
        }
        if (!string.IsNullOrWhiteSpace(jobTitle))
            sb.AppendLine($"TITLE:{Escape(jobTitle)}");
        if (!string.IsNullOrWhiteSpace(companyName))
            sb.AppendLine($"ORG:{Escape(companyName)}");
        if (!string.IsNullOrWhiteSpace(notes))
            sb.AppendLine($"NOTE:{Escape(notes)}");
        sb.AppendLine("END:VCARD");
        return sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    public static string ApplyUpdates(
        string existingVcard,
        string? displayName,
        string? givenName,
        string? surname,
        List<string>? emails,
        List<string>? phones,
        string? jobTitle,
        string? companyName,
        string? notes)
    {
        // Keep the original content lines, parameters, groups and unknown properties.
        // Only properties explicitly present in the update contract are replaced.
        var lines = Unfold(existingVcard);
        void Replace(string property, IEnumerable<string> replacements)
        {
            var indices = lines.Select((line, index) => (line, index))
                .Where(x => SplitLine(x.line).Name.Split('.').Last().Equals(property, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.index).ToList();
            var insertAt = indices.Count > 0 ? indices[0] : lines.FindIndex(l => l.Equals("END:VCARD", StringComparison.OrdinalIgnoreCase));
            if (insertAt < 0) throw new FormatException("Missing END:VCARD.");
            foreach (var index in indices.AsEnumerable().Reverse()) lines.RemoveAt(index);
            lines.InsertRange(insertAt, replacements);
        }
        void Text(string property, string? value)
        {
            if (value is not null) Replace(property, [$"{property}:{Escape(value)}"]);
        }
        Text("FN", displayName);
        if (givenName is not null || surname is not null)
        {
            var name = lines.FirstOrDefault(l => SplitLine(l).Name.Equals("N", StringComparison.OrdinalIgnoreCase));
            var parts = name is null ? new List<string>() : SplitStructured(SplitLine(name).Value);
            while (parts.Count < 5) parts.Add("");
            if (surname is not null) parts[0] = Escape(surname);
            if (givenName is not null) parts[1] = Escape(givenName);
            Replace("N", ["N:" + string.Join(";", parts)]);
        }
        if (emails is not null) Replace("EMAIL", emails.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => $"EMAIL;TYPE=INTERNET:{Escape(e.Trim())}"));
        if (phones is not null) Replace("TEL", phones.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => $"TEL;TYPE=VOICE:{Escape(e.Trim())}"));
        Text("TITLE", jobTitle);
        Text("ORG", companyName);
        Text("NOTE", notes);
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static List<string> SplitStructured(string value)
    {
        var parts = new List<string>();
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\') { i++; continue; }
            if (value[i] != ';') continue;
            parts.Add(value[start..i]);
            start = i + 1;
        }
        parts.Add(value[start..]);
        return parts;
    }

    private static List<string> Unfold(string vcard)
    {
        var normalized = vcard.Replace("\r\n", "\n").Replace('\r', '\n');
        var raw = normalized.Split('\n');
        var lines = new List<string>();
        foreach (var line in raw)
        {
            if (line.Length == 0)
                continue;
            if ((line[0] is ' ' or '\t') && lines.Count > 0)
                lines[^1] += line[1..];
            else
                lines.Add(line);
        }
        return lines;
    }

    private static (string Name, string Params, string Value) SplitLine(string line)
    {
        var colon = line.IndexOf(':');
        if (colon < 0)
            return (line, "", "");

        var left = line[..colon];
        var value = line[(colon + 1)..];
        var semi = left.IndexOf(';');
        if (semi < 0)
            return (left, "", value);
        return (left[..semi], left[(semi + 1)..], value);
    }

    private static string? TypeFromParams(string paramsPart)
    {
        var match = TypeParam().Match(paramsPart);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    private static string Escape(string value) =>
        value.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\\", "\\\\").Replace("\n", "\\n").Replace(",", "\\,").Replace(";", "\\;");

    /// <summary>
    /// Reverses <see cref="Escape"/> in one pass, so an escaped backslash followed by "n"
    /// stays a backslash and an "n" rather than becoming a newline.
    /// </summary>
    private static string Unescape(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                sb.Append(next is 'n' or 'N' ? '\n' : next);
            }
            else
            {
                sb.Append(value[i]);
            }
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"TYPE=([^;]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TypeParam();
}
