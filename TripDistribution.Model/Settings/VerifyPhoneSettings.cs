namespace TripDistribution.Model.Settings;

public class VerifyPhoneSettings
{
    public string API_KEY { get; set; } = string.Empty;
    public string API_URL { get; set; } = "https://api.veriphone.io/v2/verify";
}
