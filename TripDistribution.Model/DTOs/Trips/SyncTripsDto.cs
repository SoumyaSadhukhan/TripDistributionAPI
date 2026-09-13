using System.Collections.Generic;
namespace TripDistribution.Model.DTOs.Trips
{
    public class SyncTripsDto
    {
        public string UserId { get; set; } = string.Empty;
        public System.Text.Json.JsonElement Trips { get; set; } = new();
    }
}

