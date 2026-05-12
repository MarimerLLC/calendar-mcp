# Contacts

Contact tools are available on Microsoft 365, Google (People API),
Outlook.com, and optionally JSON-file accounts. IMAP and ICS accounts
do not expose contacts.

Always confirm capability via `list_accounts` before calling a contact
tool against an account.

## Tool reference

### `get_contacts(accountId?, count=50)`

Lists contacts. Fans out across contact-capable accounts when
`accountId` is omitted. Returns lightweight metadata.

### `search_contacts(query, accountId?, count=20)`

Full-text search across name, email, company. Fans out across all
contact-capable accounts when `accountId` is omitted.

### `get_contact_details(accountId, contactId)`

Returns the full contact record including all email addresses, phone
numbers, addresses, and notes.

### `create_contact(displayName, accountId?, givenName?, surname?, email?, phone?, jobTitle?, companyName?, notes?)`

- `displayName` is required.
- `email` and `phone` accept a single value or a comma-separated list.
- Omitting `accountId` falls back to "first configured account" or
  smart routing — pass it explicitly if you care which account ends up
  with the contact.

### `update_contact(accountId, contactId, ...)`

Same shape as `create_contact` but with `contactId` and all other fields
optional. Pass only what changes.

### `delete_contact(accountId, contactId)`

Permanent on most providers; no per-tool soft-delete or recovery.

## Common patterns

### Find a contact by name or company

```
search_contacts(query="Acme")    // fans out across accounts
→ examine results, pick the right one
→ get_contact_details(accountId, contactId) for full info
```

### Promote an email sender to a contact

```
get_email_details(accountId, emailId)    // get from, fromName
search_contacts(query=fromEmail)        // check for an existing record
→ if no match:
   create_contact(
     accountId=<contact-capable account>,
     displayName=fromName,
     email=fromEmail
   )
```

When picking which account to store the new contact in, prefer the
same account that received the email (consistent persona), or ask the
user.

### Bulk import

There is no bulk-create tool. Call `create_contact` once per record;
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
