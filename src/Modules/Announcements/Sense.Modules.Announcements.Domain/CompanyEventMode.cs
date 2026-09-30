namespace Sense.Modules.Announcements.Domain;

/// <summary>Where a company event takes place (E4): requires <see cref="CompanyEvent.Location"/> when onsite, <see cref="CompanyEvent.OnlineUrl"/> when online. Null on legacy events.</summary>
public enum CompanyEventMode
{
    Onsite = 0,
    Online = 1,
}
