# Scenarios

End-to-end workflows that wire multiple tools together. Read the
relevant single-domain guide (`email`, `calendar`, `contacts`,
`attachments`) for tool-by-tool details; this guide focuses on
sequencing.

Every scenario assumes you have already called `list_accounts` and
know which `accountId` to use for each step.

## Prompts cover several of these end-to-end

If the MCP client supports prompts, prefer the prompt over the manual
sequence — it's a single invocation that expands to an equivalent
plan and reduces the chance of mis-ordering steps:

| Scenario below | Equivalent prompt |
|---|---|
| 1. Triage the morning inbox | `email_triage` |
| 2. Schedule a meeting from an email thread | `schedule_meeting` (after extracting attendees from the thread) |
| 3. Forward an email with attachments | `forward_with_attachments` |
| 4. Respond to a meeting invite | `respond_to_invite` |
| 6. Bulk unsubscribe from marketing emails | `bulk_unsubscribe` |
| 7. Daily summary across all accounts | `daily_briefing` (today) or `week_ahead` (7 days) |
| 8. Add the sender of an email as a contact | `contact_summary` (search-first variant) |
| 10. Cross-account search for a topic | `find_emails_about` |

The remaining scenarios (free-busy approximation, re-routing
misdirected mail) don't have a prompt yet — drive those with the tools
directly.

## 1. Triage the morning inbox

Goal: quickly classify unread mail across all accounts and act on
obvious clusters.

```
1. get_emails(unreadOnly=true, count=100)
   → fans out across all accounts, newest first

2. (Optional, for a richer summary)
   get_contextual_email_summary(
     unreadOnly=true,
     includeBodyPreview=true
   )
   → returns topic clusters + persona/account-mismatch analysis

3. Group the result mentally (or via topic clusters):
   - Newsletters / marketing → unsubscribe_from_email (high signal-to-noise)
                              then bulk_delete_emails
   - Meeting invites → keep, see scenario 6
   - Action-required → keep, address one-by-one
   - Notifications → bulk_mark_emails_as_read

4. Apply in batches:
   bulk_delete_emails(items=[ {accountId, emailId}, ... ])
   bulk_mark_emails_as_read(items=[ ... ])
```

## 2. Schedule a meeting from an email thread

Goal: someone proposed a time over email; create the event with the
right people.

```
1. get_email_details(accountId, emailId)
   → extract: from, to, cc → these are the candidate attendees

2. Determine your free/busy:
   get_calendar_events(
     timeZone="<user TZ>",
     startDate=<proposed day>,
     endDate=<proposed day>,
     accountId=<calendar account>
   )
   → confirm the proposed slot is free; if not, suggest alternatives

3. create_event(
     accountId=<calendar account>,
     subject="<derived from email subject>",
     start="<proposed start ISO>",
     end="<proposed end ISO>",
     timeZone="<user TZ>",
     attendees=[from, ...to, ...cc],
     body="<short context referencing the email thread>"
   )

4. (Optional) send_email(
     to=[from, ...to, ...cc],
     subject="Re: <original>",
     body="Scheduled — calendar invite incoming.",
     accountId=<same account the original was received on>
   )
```

## 3. Forward an email with attachments

Goal: send a received email's attachment(s) to someone else.

See `attachments` for the full pattern. Short form:

```
1. get_email_details(accountId, emailId)
   → response.attachments[].attachmentId   (provider-side IDs)

2. For each attachment to forward:
   get_email_attachment(accountId, emailId, attachmentId, mode="stash")
   → response.attachmentId                  (server-stash ID)

3. send_email(
     to=["forward@example.com"],
     subject="Fwd: <original>",
     body="<context>",
     accountId=<same account or pick deliberately>,
     attachments=[
       { "attachmentId": "<stash-id-1>" },
       { "attachmentId": "<stash-id-2>" }
     ]
   )
```

## 4. Respond to a meeting invite

```
1. get_emails(unreadOnly=true) or get_calendar_events(...)
   → find the pending invite

2. get_calendar_event_details(accountId, calendarId, eventId, timeZone)
   → review attendees, conflicts, agenda

3. (Optional) get_calendar_events over the event's window
   → check for overlaps

4. respond_to_event(
     eventId,
     response="accept",  // or "tentative" / "decline"
     accountId=<the account that received the invite>,
     calendarId=<from event details>,
     comment="<optional note to organizer>"
   )
```

## 5. Find time across multiple participants

There is no free-busy/availability tool. Approximate it:

```
1. get_calendar_events(timeZone="...", startDate, endDate, accountId=<own>)
   → identify your free slots

2. For each external participant where you have visibility:
   get_calendar_events(timeZone="...", startDate, endDate, accountId=<their account if you have access>)

3. Intersect manually; propose top N candidate slots.

4. send_email or create_event with proposed times.
```

For colleagues whose calendars you cannot read, fall back to an email
proposal.

## 6. Bulk unsubscribe from marketing emails

```
1. search_emails(query="unsubscribe", count=50)
   → likely-marketing candidates

2. For each:
   get_unsubscribe_info(accountId, emailId)
   → confirms List-Unsubscribe presence and which methods exist

3. unsubscribe_from_email(accountId, emailId, method="auto")
   → executes the unsubscribe (one-click POST when available)

4. After processing the batch:
   bulk_delete_emails(items=[ {accountId, emailId for each processed}, ... ])
   → tidy up historical clutter

Or skip step 2 and just call unsubscribe_from_email with method="auto" —
it returns a clear error if no method is available, which you can
ignore for those entries.
```

## 7. Daily summary across all accounts

```
1. get_contextual_email_summary(
     countPerAccount=50,
     unreadOnly=false,
     includeBodyPreview=true,
     maxSamplesPerCluster=3
   )
   → topic clusters, persona contexts, account mismatches

2. get_calendar_events(
     timeZone=<user TZ>,
     startDate=<today>,
     endDate=<today>,
     accountId=<each calendar-capable account>
   )
   → today's agenda per persona

3. Compose a brief: top clusters, urgent items, today's meetings,
   notable account mismatches.
```

`get_contextual_email_summary` is heavy — once per session is fine,
don't loop it.

## 8. Add the sender of an email as a contact

```
1. get_email_details(accountId, emailId)
   → from, fromName

2. search_contacts(query=fromEmail)
   → make sure they don't already exist

3. If not found:
   create_contact(
     accountId=<same account as the email, or pick deliberately>,
     displayName=fromName,
     email=fromEmail
   )
```

## 9. Move a misdirected email to the right account

`get_contextual_email_summary` reports `accountMismatches`. There is
no cross-account move tool, but you can forward then delete:

```
For each mismatch entry:
  get_email_details(receivedOnAccount, emailId)
  → get full body + attachments

  Forward to yourself on expectedAccount:
    Optionally stash attachments via get_email_attachment
    send_email(
      to=[<your address on expectedAccount>],
      subject="Re-routed: " + original.subject,
      body=original.body,
      accountId=receivedOnAccount,
      attachments=[ ... ]
    )

  delete_email(receivedOnAccount, emailId)
```

This is destructive; only do it after user confirmation.

## 10. Cross-account search for a topic

```
search_emails(query="<topic>")    // omit accountId → fans out
→ results carry their own accountId; preserve it for follow-up calls
```

For richer cross-account analysis (which account, what cluster, who's
been talking about it):

```
get_contextual_email_summary(topics="<topic>")
```
