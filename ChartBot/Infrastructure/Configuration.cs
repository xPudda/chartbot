using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace ChartBot.Infrastructure
{
    public class Configuration
    {
        [JsonProperty("token")]
        public string Token { get; set; }

        [JsonProperty("askToUserId")]
        public long AskToUserId { get; set; }

        [JsonProperty("sendNotificationOnMessage")]
        public bool SendNotificationOnMessage { get; set; }

        [JsonPropertyName("admins")]
        public long[] Admins { get; set; }
    }
}
