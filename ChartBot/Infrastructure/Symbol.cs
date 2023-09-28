using Newtonsoft.Json;
using System.Collections.Generic;

namespace ChartBot.Infrastructure
{
    public class ResultSet
    {
        [JsonProperty("ResultSet")]
        public TranslateResult Symbols { get; set; }
    }

    public class TranslateResult
    {
        [JsonProperty("Result")]
        public IEnumerable<Symbol> Symbols { get; set; }
    }

    public class Symbol
    {
        [JsonProperty("symbol")]
        public string SymbolName { get; set; }
    }

    public class TickerSymbol
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }
}
