using System;

namespace ChartBot.Infrastructure
{
    public class ChartRequest
    {
        public Guid Guid { get; set; }
        public string UtenteRichiedente { get; set; }
        public string Simbolo { get; set; }
        public string Timeframe { get; set; }
        public string Note { get; set; }
        public long ChatId { get; set; }
        public int MessageId { get; set; }
        public string ReferMessageUrl { get; set; }
    }
}
