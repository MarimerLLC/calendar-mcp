namespace CalendarMcp.Core.Models;

/// <summary>
/// Unified contact representation across all providers
/// </summary>
public class Contact
{
    /// <summary>
    /// Provider-specific contact ID
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Account ID this contact belongs to
    /// </summary>
    public required string AccountId { get; init; }

    /// <summary>
    /// Full display name
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// First/given name
    /// </summary>
    public string GivenName { get; init; } = string.Empty;

    /// <summary>
    /// Last/family name
    /// </summary>
    public string Surname { get; init; } = string.Empty;

    /// <summary>
    /// Email addresses
    /// </summary>
    public List<ContactEmail> EmailAddresses { get; init; } = new();

    /// <summary>
    /// Phone numbers
    /// </summary>
    public List<ContactPhone> PhoneNumbers { get; init; } = new();

    /// <summary>
    /// Job title
    /// </summary>
    public string JobTitle { get; init; } = string.Empty;

    /// <summary>
    /// Company/organization name
    /// </summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>
    /// Department
    /// </summary>
    public string Department { get; init; } = string.Empty;

    /// <summary>
    /// Physical addresses
    /// </summary>
    public List<ContactAddress> Addresses { get; init; } = new();

    /// <summary>
    /// Birthday
    /// </summary>
    public DateTime? Birthday { get; init; }

    /// <summary>
    /// Notes/comments about the contact
    /// </summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>
    /// Contact groups/labels
    /// </summary>
    public List<string> Groups { get; init; } = new();

    /// <summary>
    /// Provider-specific version tag (used by Google People API for concurrency)
    /// </summary>
    public string? Etag { get; init; }

    /// <summary>
    /// When the contact was created
    /// </summary>
    public DateTime? CreatedDateTime { get; init; }

    /// <summary>
    /// When the contact was last modified
    /// </summary>
    public DateTime? LastModifiedDateTime { get; init; }
}

/// <summary>
/// Contact email address with label
/// </summary>
public class ContactEmail
{
    /// <summary>
    /// Email address
    /// </summary>
    public required string Address { get; init; }

    /// <summary>
    /// Label: "home", "work", "other"
    /// </summary>
    public string Label { get; init; } = "other";
}

/// <summary>
/// Contact phone number with label
/// </summary>
public class ContactPhone
{
    /// <summary>
    /// Phone number
    /// </summary>
    public required string Number { get; init; }

    /// <summary>
    /// Label: "mobile", "home", "work", "other"
    /// </summary>
    public string Label { get; init; } = "other";
}

/// <summary>
/// Contact physical address
/// </summary>
public class ContactAddress
{
    /// <summary>
    /// Street address
    /// </summary>
    public string Street { get; init; } = string.Empty;

    /// <summary>
    /// City
    /// </summary>
    public string City { get; init; } = string.Empty;

    /// <summary>
    /// State/province
    /// </summary>
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// Postal/ZIP code
    /// </summary>
    public string PostalCode { get; init; } = string.Empty;

    /// <summary>
    /// Country/region
    /// </summary>
    public string Country { get; init; } = string.Empty;

    /// <summary>
    /// Label: "home", "business", "other"
    /// </summary>
    public string Label { get; init; } = "other";
}
