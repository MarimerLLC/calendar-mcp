# Scenarios

End-to-end workflows that wire multiple tools together. Read the
relevant single-domain guide (`email`, `calendar`, `contacts`,
`attachments`) for tool-by-tool details; this guide focuses on
sequencing.

Every scenario assumes you have already called `ListAccounts` and
know which `accountId` to use for each step.

## 1. Triage the morning inbox

Goal: quickly classify unread mail across all accounts and act on
obvious clusters.

```
1. GetEmails(unreadOnly=true, count=100)
   → fans out across all accounts, newest first

2. (Optional, for a richer summary)
   get_contextual_email_summary(
     unreadOnly=true,
     includeBodyPreview=true
   )
   → returns topic clusters + persona/account-mismatch analysis

3. Group the result mentally (or via topic clusters):
   - Newsletters / marketing → UnsubscribeFromEmail (high signal-to-noise)
                              then BulkDeleteEmails
   - Meeting invites → keep, see scenario 6
   - Action-required → keep, address one-by-one
   - Notifications → BulkMarkEmailsAsRead

4. Apply in batches:
   BulkDeleteEmails(items=[ {accountId, emailId}, ... ])
   BulkMarkEmailsAsRead(items=[ ... ])
```

## 2. Schedule a meeting from an email thread

Goal: someone proposed a time over email; create the event with the
right people.

```
1. GetEmailDetails(accountId, emailId)
   → extract: from, to, cc → these are the candidate attendees

2. Determine your free/busy:
   GetCalendarEvents(
     timeZone="<user TZ>",
     startDate=<proposed day>,
     endDate=<proposed day>,
     accountId=<calendar account>
   )
   → confirm the proposed slot is free; if not, suggest alternatives

3. CreateEvent(
     accountId=<calendar account>,
     subject="<derived from email subject>",
     start="<proposed start ISO>",
     end="<proposed end ISO>",
     timeZone="<user TZ>",
     attendees=[from, ...to, ...cc],
     body="<short context referencing the email thread>"
   )

4. (Optional) SendEmail(
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
1. GetEmailDetails(accountId, emailId)
   → response.attachments[].attachmentId   (provider-side IDs)

2. For each attachment to forward:
   GetEmailAttachment(accountId, emailId, attachmentId, mode="stash")
   → response.attachmentId                  (server-stash ID)

3. SendEmail(
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
1. GetEmails(unreadOnly=true) or GetCalendarEvents(...)
   → find the pending invite

2. GetCalendarEventDetails(accountId, calendarId, eventId, timeZone)
   → review attendees, conflicts, agenda

3. (Optional) GetCalendarEvents over the event's window
   → check for overlaps

4. RespondToEvent(
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
1. GetCalendarEvents(timeZone="...", startDate, endDate, accountId=<own>)
   → identify your free slots

2. For each external participant where you have visibility:
   GetCalendarEvents(timeZone="...", startDate, endDate, accountId=<their account if you have access>)

3. Intersect manually; propose top N candidate slots.

4. SendEmail or CreateEvent with proposed times.
```

For colleagues whose calendars you cannot read, fall back to an email
proposal.

## 6. Bulk unsubscribe from marketing emails

```
1. SearchEmails(query="unsubscribe", count=50)
   → likely-marketing candidates

2. For each:
   GetUnsubscribeInfo(accountId, emailId)
   → confirms List-Unsubscribe presence and which methods exist

3. UnsubscribeFromEmail(accountId, emailId, method="auto")
   → executes the unsubscribe (one-click POST when available)

4. After processing the batch:
   BulkDeleteEmails(items=[ {accountId, emailId for each processed}, ... ])
   → tidy up historical clutter

Or skip step 2 and just call UnsubscribeFromEmail with method="auto" —
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

2. GetCalendarEvents(
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
1. GetEmailDetails(accountId, emailId)
   → from, fromName

2. SearchContacts(query=fromEmail)
   → make sure they don't already exist

3. If not found:
   CreateContact(
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
  GetEmailDetails(receivedOnAccount, emailId)
  → get full body + attachments

  Forward to yourself on expectedAccount:
    Optionally stash attachments via GetEmailAttachment
    SendEmail(
      to=[<your address on expectedAccount>],
      subject="Re-routed: " + original.subject,
      body=original.body,
      accountId=receivedOnAccount,
      attachments=[ ... ]
    )

  DeleteEmail(receivedOnAccount, emailId)
```

This is destructive; only do it after user confirmation.

## 10. Cross-account search for a topic

```
SearchEmails(query="<topic>")    // omit accountId → fans out
→ results carry their own accountId; preserve it for follow-up calls
```

For richer cross-account analysis (which account, what cluster, who's
been talking about it):

```
get_contextual_email_summary(topics="<topic>")
```
