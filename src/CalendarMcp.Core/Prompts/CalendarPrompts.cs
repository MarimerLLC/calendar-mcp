using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Prompts;

/// <summary>
/// MCP prompt templates for calendar-related workflows
/// </summary>
[McpServerPromptType]
public sealed class CalendarPrompts
{
    [McpServerPrompt(Name = "daily_briefing"), Description(
        "Prepares a daily briefing covering today's calendar events and unread emails across all accounts. " +
        "Invoke this at the start of the day to get a structured overview of your schedule and inbox.")]
    public string DailyBriefing(
        [Description("IANA timezone name for displaying times (e.g. 'America/Chicago', 'Europe/London'). Required.")] string timeZone)
    {
        return $"""
            Please prepare my daily briefing for today. Follow these steps in order:

            1. Call list_accounts to discover all configured accounts.
            2. Call get_calendar_events with timeZone="{timeZone}", startDate=today, endDate=today for each account (or omit accountId to query all at once).
            3. Call get_emails with unreadOnly=true to retrieve unread messages across all accounts.

            Present the results as a structured briefing:

            ## Today's Schedule
            List all events sorted by start time, including time (in {timeZone}), title, location, and attendees.
            Note any back-to-back meetings or conflicts.

            ## Inbox Summary
            Total unread count by account, then highlight any messages that appear time-sensitive or require a response today.

            ## Action Items
            Summarise any meetings requiring preparation or emails requiring a reply.
            """;
    }

    [McpServerPrompt(Name = "week_ahead"), Description(
        "Provides a week-at-a-glance summary of upcoming calendar events for the next 7 days. " +
        "Useful for planning and spotting busy days in advance.")]
    public string WeekAhead(
        [Description("IANA timezone name for displaying times (e.g. 'America/Chicago', 'Europe/London'). Required.")] string timeZone)
    {
        return $"""
            Please prepare a week-ahead summary. Follow these steps:

            1. Call list_accounts to discover all configured accounts.
            2. Call get_calendar_events with timeZone="{timeZone}", startDate=today, endDate=7 days from today.

            Present the results grouped by day:

            ## Week Ahead
            For each day that has events, show the date as a heading and list events with their time ({timeZone}), title, location, and key attendees.
            Skip days with no events.

            ## Highlights
            - Identify any unusually busy days.
            - Note any multi-day events or all-day events.
            - Flag any days with no events (free days).
            """;
    }

    [McpServerPrompt(Name = "schedule_meeting"), Description(
        "Guides the assistant to find available times and create a calendar event. " +
        "Provide the meeting title, duration, and attendee email addresses to get started.")]
    public string ScheduleMeeting(
        [Description("Title or subject for the meeting.")] string title,
        [Description("Duration of the meeting in minutes (e.g. 30, 60).")] int durationMinutes,
        [Description("Comma-separated list of attendee email addresses.")] string attendees,
        [Description("IANA timezone name (e.g. 'America/Chicago'). Required.")] string timeZone,
        [Description("Preferred date to schedule (ISO 8601, e.g. '2026-03-15'). Defaults to this week.")] string? preferredDate = null)
    {
        var dateHint = preferredDate != null
            ? $"Focus on {preferredDate} first, then expand to nearby days if needed."
            : "Focus on the next 5 business days.";

        return $"""
            Please help me schedule a meeting. Here are the details:

            - Title: {title}
            - Duration: {durationMinutes} minutes
            - Attendees: {attendees}
            - Timezone: {timeZone}

            Follow these steps:

            1. Call list_accounts to identify which account to create the event on.
            2. Call get_calendar_events with timeZone="{timeZone}" to see existing events. {dateHint}
            3. Propose 3 available time slots that avoid conflicts, prefering business hours (9 AM–5 PM {timeZone}).
            4. Ask me to confirm which slot to use.
            5. Once confirmed, call create_event with the chosen time, title "{title}", duration {durationMinutes} minutes, and attendees: {attendees}.
            6. Confirm the event was created and share the details.
            """;
    }
}
