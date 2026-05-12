# Contacts

Contact tools are available on Microsoft 365, Google (People API),
Outlook.com, and optionally JSON-file accounts. IMAP and ICS accounts
do not expose contacts.

Always confirm capability via `ListAccounts` before calling a contact
tool against an account.

## Tool reference

### `GetContacts(accountId?, count=50)`

Lists contacts. Fans out across contact-capable accounts when
`accountId` is omitted. Returns lightweight metadata.

### `SearchContacts(query, accountId?, count=20)`

Full-text search across name, email, company. Fans out across all
contact-capable accounts when `accountId` is omitted.

### `GetContactDetails(accountId, contactId)`

Returns the full contact record including all email addresses, phone
numbers, addresses, and notes.

### `CreateContact(displayName, accountId?, givenName?, surname?, email?, phone?, jobTitle?, companyName?, notes?)`

- `displayName` is required.
- `email` and `phone` accept a single value or a comma-separated list.
- Omitting `accountId` falls back to "first configured account" or
  smart routing — pass it explicitly if you care which account ends up
  with the contact.

### `UpdateContact(accountId, contactId, ...)`

Same shape as `CreateContact` but with `contactId` and all other fields
optional. Pass only what changes.

### `DeleteContact(accountId, contactId)`

Permanent on most providers; no per-tool soft-delete or recovery.

## Common patterns

### Find a contact by name or company

```
SearchContacts(query="Acme")    // fans out across accounts
→ examine results, pick the right one
→ GetContactDetails(accountId, contactId) for full info
```

### Promote an email sender to a contact

```
GetEmailDetails(accountId, emailId)    // get from, fromName
SearchContacts(query=fromEmail)        // check for an existing record
→ if no match:
   CreateContact(
     accountId=<contact-capable account>,
     displayName=fromName,
     email=fromEmail
   )
```

When picking which account to store the new contact in, prefer the
same account that received the email (consistent persona), or ask the
user.

### Bulk import

There is no bulk-create tool. Call `CreateContact` once per record;
use the per-call response to detect duplicates.

## Pitfalls

- **Provider quirks**: Google's People API normalizes phone numbers
  and email types differently than Graph; round-tripping a contact
  between providers can change formatting.
- **`displayName` only**: when only `displayName` is provided, some
  providers (Google) parse it into given/family name heuristically.
  Pass `givenName`/`surname` explicitly when you have them.
- **Duplicate detection** is your responsibility — providers don't
  reject duplicates on create.
