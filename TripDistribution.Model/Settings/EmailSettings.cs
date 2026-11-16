namespace TripDistribution.Model.Settings;

public class EmailSettings
{
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string SmtpPassword { get => Password; set => Password = value; }
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "CoreCraft";
}

