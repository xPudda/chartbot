using ChartBot.Infrastructure;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;
using YahooFinanceApi;
using File = System.IO.File;

namespace ChartBot.Brain
{
    public class Bot : IDisposable
    {
        private CancellationTokenSource _cancellationTokenSource;
        private CancellationToken _cancellationToken;
        private Configuration _configuration;
        private ITelegramBotClient _telegramBotClient;
        private List<ChartRequest> _request;
        private bool _disposedValue;

        private bool _waitForUserInput;
        private Chat _waitForUserInputChat;
        private Guid? _waitForUserInputRequest;
        private CancellationTokenSource _waitForUserInputCancellationTokenSource;

        public Bot()
        {
            Inizializza();
        }

        #region Invio messaggi

        private async Task ReponseMessageAsync(MessageEventArgs e)
        {
            if (_waitForUserInput && _waitForUserInputChat.Id == e.Message.Chat.Id)
            {
                Message message;
                if (e.Message.Type == MessageType.Photo || e.Message.Type == MessageType.Document)
                {
                    await SendChartToRequestUserAsync(e.Message);

                    DeleteRequest(_waitForUserInputRequest.Value);

                    message = await _telegramBotClient.SendTextMessageAsync(e.Message.Chat, "Chart inoltrata.", parseMode: ParseMode.Markdown);
                }
                else
                {
                    message = await _telegramBotClient.SendTextMessageAsync(e.Message.Chat, "Il formato inviato non è corretto. Inviare una foto o un documento.", parseMode: ParseMode.Markdown);
                }

                _waitForUserInputCancellationTokenSource.Cancel();

                _ = Task.Delay(TimeSpan.FromSeconds(5))
                            .ContinueWith(_ => _telegramBotClient.DeleteMessageAsync(message.Chat, message.MessageId))
                            .ContinueWith(_ => ResponseRichiesteAsync(message.Chat));
            }
            else if (!string.IsNullOrEmpty(e.Message.Text))
            {
                if (e.Message.Chat.Type == ChatType.Private)
                {
                    Console.WriteLine($"@{e.Message.Chat.Username}: {e.Message.Text}");

                    if (e.Message.Text.Equals("/start", StringComparison.InvariantCultureIgnoreCase) || e.Message.Text.Equals("/help", StringComparison.InvariantCultureIgnoreCase))
                    {
                        await ResponseHelpAsync(e);
                    }
                    else if (e.Message.Text.Equals("/richieste", StringComparison.InvariantCultureIgnoreCase) && e.Message.From.Id.In(_configuration.Admins))
                    {
                        await ResponseRichiesteAsync(e.Message.Chat);
                    }
                    else if (e.Message.Text.StartsWith("/chart", StringComparison.InvariantCultureIgnoreCase))
                    {
                        (bool success, string simbolo, string timeframe, string note) = ParseMessage(e.Message.Text);

                        if (success)
                        {
                            ChartRequest req = AddRequest(simbolo, timeframe, note, $"https://t.me/{e.Message.From.Username}", e.Message.From.Username, e.Message.MessageId);

                            await ResponseChartRequestAsync(req, e);
                        }
                        else
                        {
                            await ResponseFormatoNonValidoAsync(e);
                        }
                    }
                }
                else if (e.Message.Chat.Type is ChatType.Group or ChatType.Supergroup)
                {
                    if (e.Message.Text.EndsWith("@dovahkiinChartBot") &&
                            (e.Message.Text.StartsWith("/start", StringComparison.InvariantCultureIgnoreCase) || e.Message.Text.StartsWith("/help", StringComparison.InvariantCultureIgnoreCase)))
                    {
                        Console.WriteLine($"{e.Message.Chat.Title} - @{e.Message.Chat}: {e.Message.Text}");

                        await ResponseHelpAsync(e);
                    }
                    else if (e.Message.Text.StartsWith("/chart", StringComparison.InvariantCultureIgnoreCase))
                    {
                        (bool success, string simbolo, string timeframe, string note) = ParseMessage(e.Message.Text);

                        if (success)
                        {
                            ChartRequest req = AddRequest(
                                simbolo, timeframe, note, $"https://t.me/{e.Message.Chat.Username ?? e.Message.Chat.Title}/{e.Message.MessageId}", e.Message.From.Username, e.Message.Chat.Id, e.Message.MessageId);

                            await ResponseChartRequestAsync(req, e);
                        }
                        else
                        {
                            await ResponseFormatoNonValidoAsync(e);
                        }
                    }
                }
            }
        }

        private async Task SendChartToRequestUserAsync(Message message)
        {
            ChartRequest request = _request.FirstOrDefault(r => r.Guid == _waitForUserInputRequest);

            if (request == null)
            {
                return;
            }

            if (message.Type == MessageType.Photo)
            {
                await _telegramBotClient.SendPhotoAsync(
                    new ChatId(request.ChatId), new InputOnlineFile(message.Photo.FirstOrDefault().FileId), caption: message.Caption, parseMode: ParseMode.Markdown, replyToMessageId: request.MessageId);
            }
            else if (message.Type == MessageType.Document)
            {
                await _telegramBotClient.SendDocumentAsync(new ChatId(request.ChatId), new InputOnlineFile(message.Document.FileId), caption: message.Caption, parseMode: ParseMode.Markdown, replyToMessageId: request.MessageId);
            }
        }

        private async Task RevertWaitForUserInput(Chat chat, Message message)
        {
            try
            {
                CancellationToken cancellationToken = _waitForUserInputCancellationTokenSource.Token;

                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(100, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
            {
                _waitForUserInput = false;

                _waitForUserInputChat = null;

                _waitForUserInputRequest = null;

                await _telegramBotClient.DeleteMessageAsync(chat, message.MessageId);
            }
        }

        private async Task ResponseSendChartAsync(Chat chat, Guid guid)
        {
            Message message = await _telegramBotClient.SendTextMessageAsync(chat, "Invia ora entro 45 secondi la chart.");

            _waitForUserInput = true;

            _waitForUserInputChat = chat;

            _waitForUserInputRequest = guid;

            _waitForUserInputCancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(45));

            _ = RevertWaitForUserInput(chat, message);
        }

        private Task ResponseRichiesteAsync(Chat chat)
        {
            if (!_request.Any())
            {
                return _telegramBotClient.SendTextMessageAsync(chat, "Nessuna richiesta attiva.");
            }

            IEnumerable<IEnumerable<InlineKeyboardButton>> buttons = _request.Partition(4).Select(reqs => reqs.Select(req => new InlineKeyboardButton
            {
                Text = req.Simbolo,
                CallbackData = $"request/{req.Guid}"
            }));

            buttons = buttons.Append(Enumerable.Empty<InlineKeyboardButton>().Append(new InlineKeyboardButton
            {
                Text = "❌ Cancella tutto",
                CallbackData = "deleteAll"
            }));

            return _telegramBotClient.SendTextMessageAsync(chat, "Richieste attive ora:", replyMarkup: new InlineKeyboardMarkup(buttons));
        }

        private Task ResponseChartRequestAsync(ChartRequest request, MessageEventArgs e)
        {
            return _telegramBotClient.SendTextMessageAsync(new ChatId(e.Message.Chat.Id),
                $"{(_configuration.SendNotificationOnMessage ? $"[Chart richiesta!](tg://user?id={_configuration.AskToUserId})" : "Chart richiesta!")} \n\nSimbolo 📈: *{request.Simbolo}*\nTimeframe ⌛: *{request.Timeframe}*{(!string.IsNullOrEmpty(request.Note) ? $"\nNote 📜: *{request.Note}*" : "")}",
                    parseMode: ParseMode.Markdown, replyToMessageId: e.Message.MessageId);
        }

        private Task ResponseFormatoNonValidoAsync(MessageEventArgs e)
        {
            return _telegramBotClient.SendTextMessageAsync(new ChatId(e.Message.Chat.Id),
                "Il formato scritto non è corretto! Utilizza /help per conoscere come richiedere una chart.", replyToMessageId: e.Message.MessageId);
        }

        private (bool success, string simbolo, string timeframe, string note) ParseMessage(string message)
        {
            Regex regex = new(@"\/chart(@dovahkiinChartBot)?\s{1}(?<simbolo>.{1,})\s{1}(?<timeframe>\d{1,2}\w{1})\s{0,1}(?<note>.{0,})");

            Match match = regex.Match(message);

            return !match.Success
                ? (false, null, null, null)
                : (true, match.Groups["simbolo"].Value, match.Groups["timeframe"].Value, match.Groups["note"].Value);
        }

        private Task ResponseHelpAsync(MessageEventArgs e)
        {
            return _telegramBotClient.SendTextMessageAsync(new ChatId(e.Message.Chat.Id),
                $"Per richiedere una chart di una 'stock/crypto/materia prima' digitare in questo ordine: " +
                $"\n\n\t\t- /chart (obbligatorio)" +
                $"\n\t\t- simbolo dello stock/crypto/materia prima (obbligatorio)" +
                $"\n\t\t- timeframe (obbligatorio, nel formato 30s, 15m, 1h, 4h, 1d, 1w)" +
                $"\n\t\t- note/istruzioni aggiuntive (facoltativo)" +
                $"\n\nOgni elemento deve essere diviso da uno spazio. Tutto ciò specificato dopo il timeframe viene considerato come nota aggiuntiva. " +
                $"Es.:\n\n\t\t/chart MSFT 4h\n\t\t/chart NKLA 1h\n\t\t/chart DOT 1w\n\t\t/chart OIL 15m Evidenzia le doji star candle", replyToMessageId: e.Message.MessageId);
        }

        private ChartRequest AddRequest(string simbolo, string timeframe, string note = null, string urlToMessage = null, string utente = null, long chatId = 0, int messageId = 0)
        {
            ChartRequest req = new()
            {
                Guid = Guid.NewGuid(),
                Simbolo = simbolo.ToUpper(),
                Timeframe = timeframe,
                Note = note,
                UtenteRichiedente = utente,
                ReferMessageUrl = urlToMessage,
                ChatId = chatId,
                MessageId = messageId
            };

            _request.Add(req);

            SalvaSuFile();

            return req;
        }

        private void DeleteRequest(Guid guid)
        {
            _request.Remove(_request.First(r => r.Guid == guid));

            SalvaSuFile();
        }

        private void DeleteAllRequests()
        {
            _request.Clear();

            SalvaSuFile();
        }

        private void SalvaSuFile()
        {
            if (File.Exists("requests.json"))
            {
                File.Delete("requests.json");
            }

            File.WriteAllText("requests.json", JsonConvert.SerializeObject(_request));
        }

        private Task SendRequestAsync(CallbackQueryEventArgs e, Guid guid)
        {
            ChartRequest request = _request.FirstOrDefault(r => r.Guid == guid);

            if (request is null)
            {
                return _telegramBotClient.SendTextMessageAsync(new ChatId(e.CallbackQuery.Message.Chat.Id), $"Nessuna richiesta trovata con guid {guid}");
            }

            List<List<InlineKeyboardButton>> keyboard = new List<List<InlineKeyboardButton>>()
            {
                new List<InlineKeyboardButton>()
                {
                    new InlineKeyboardButton
                    {
                        Text = "Vai al messaggio/utente",
                        Url = request.ReferMessageUrl
                    }
                },
                new List<InlineKeyboardButton>()
                {
                    new InlineKeyboardButton
                    {
                        Text = "❌ Cancella",
                        CallbackData = $"delete/{request.Guid}"
                    }
                },
                new List<InlineKeyboardButton>()
                {
                    new InlineKeyboardButton
                    {
                        Text = "⬅️ Indietro",
                        CallbackData = "back"
                    }
                }
            };

            if (request.ChatId != 0)
            {
                keyboard.Insert(1, new List<InlineKeyboardButton>()
                {
                    new InlineKeyboardButton
                    {
                        Text = "💬 Rispondi",
                        CallbackData = $"response/{request.Guid}"
                    }
                });
            }

            return _telegramBotClient.SendTextMessageAsync(new ChatId(e.CallbackQuery.Message.Chat.Id),
                $"\n\nSimbolo 📈: *{request.Simbolo}*\nTimeframe ⌛: *{request.Timeframe}*" +
                $"{(!string.IsNullOrEmpty(request.Note) ? $"\nNote 📜: *{request.Note}*" : "")}" +
                $"{(!string.IsNullOrEmpty(request.UtenteRichiedente) ? $"\nUtente richiedente: @{request.UtenteRichiedente.Replace("_", "\\_")}" : "")}",
                    parseMode: ParseMode.Markdown,
                    replyMarkup: new InlineKeyboardMarkup(keyboard));
        }

        private Task ConfirmRequestDeleteAsync(CallbackQueryEventArgs e, Guid guid)
        {
            return _telegramBotClient.SendTextMessageAsync(new ChatId(e.CallbackQuery.Message.Chat.Id),
                "Sei sicuro di voler cancellare la richiesta?",
                replyMarkup: new InlineKeyboardMarkup(
                    new List<List<InlineKeyboardButton>>
                    {
                        new List<InlineKeyboardButton>
                        {
                            new InlineKeyboardButton
                            {
                                Text = "❌ Annulla cancellazione",
                                CallbackData = $"reverteDelete/{guid}"
                            }
                        },
                        new List<InlineKeyboardButton>
                        {
                            new InlineKeyboardButton
                            {
                                Text = "✔️ Conferma cancellazione",
                                CallbackData = $"confirmDelete/{guid}"
                            }
                        }
                    }));
        }

        private Task ConfirmRequestDeleteAllAsync(CallbackQueryEventArgs e)
        {
            return _telegramBotClient.SendTextMessageAsync(new ChatId(e.CallbackQuery.Message.Chat.Id),
                "Sei sicuro di voler cancellare tutte le richieste?",
                replyMarkup: new InlineKeyboardMarkup(
                    new List<List<InlineKeyboardButton>>
                    {
                        new List<InlineKeyboardButton>
                        {
                            new InlineKeyboardButton
                            {
                                Text = "❌ Annulla cancellazione",
                                CallbackData = $"reverteDeleteAll"
                            }
                        },
                        new List<InlineKeyboardButton>
                        {
                            new InlineKeyboardButton
                            {
                                Text = "✔️ Conferma cancellazione",
                                CallbackData = $"confirmDeleteAll"
                            }
                        }
                    }));
        }

        private async Task ResponseCallbackAsync(CallbackQueryEventArgs e)
        {
            if (e.CallbackQuery.Data.StartsWith("request"))
            {
                await _telegramBotClient.DeleteMessageAsync(e.CallbackQuery.Message.Chat, e.CallbackQuery.Message.MessageId);

                await SendRequestAsync(e, Guid.Parse(e.CallbackQuery.Data.AsSpan(e.CallbackQuery.Data.IndexOf('/') + 1)));
            }
            else if (e.CallbackQuery.Data.StartsWith("response"))
            {
                await _telegramBotClient.DeleteMessageAsync(e.CallbackQuery.Message.Chat, e.CallbackQuery.Message.MessageId);

                Guid guid = Guid.Parse(e.CallbackQuery.Data.AsSpan(e.CallbackQuery.Data.IndexOf('/') + 1));

                await ResponseSendChartAsync(e.CallbackQuery.Message.Chat, guid);
            }
            else if (e.CallbackQuery.Data.StartsWith("back"))
            {
                await _telegramBotClient.DeleteMessageAsync(e.CallbackQuery.Message.Chat, e.CallbackQuery.Message.MessageId);

                await ResponseRichiesteAsync(e.CallbackQuery.Message.Chat);
            }
            else if (e.CallbackQuery.Data.StartsWith("deleteAll"))
            {
                await _telegramBotClient.DeleteMessageAsync(e.CallbackQuery.Message.Chat, e.CallbackQuery.Message.MessageId);

                await ConfirmRequestDeleteAllAsync(e);
            }
            else if (e.CallbackQuery.Data.StartsWith("reverteDeleteAll"))
            {
                await _telegramBotClient.DeleteMessageAsync(e.CallbackQuery.Message.Chat, e.CallbackQuery.Message.MessageId);

                await ResponseRichiesteAsync(e.CallbackQuery.Message.Chat);
            }
            else if (e.CallbackQuery.Data.StartsWith("confirmDeleteAll"))
            {
                await _telegramBotClient.DeleteMessageAsync(e.CallbackQuery.Message.Chat, e.CallbackQuery.Message.MessageId);

                DeleteAllRequests();

                await _telegramBotClient.AnswerCallbackQueryAsync(e.CallbackQuery.Id, $"Richieste cancellate.");

                await ResponseRichiesteAsync(e.CallbackQuery.Message.Chat);
            }
            else if (e.CallbackQuery.Data.StartsWith("delete"))
            {
                await _telegramBotClient.DeleteMessageAsync(e.CallbackQuery.Message.Chat, e.CallbackQuery.Message.MessageId);

                Guid guid = Guid.Parse(e.CallbackQuery.Data.AsSpan(e.CallbackQuery.Data.IndexOf('/') + 1));

                await ConfirmRequestDeleteAsync(e, guid);
            }
            else if (e.CallbackQuery.Data.StartsWith("confirmDelete"))
            {
                await _telegramBotClient.DeleteMessageAsync(e.CallbackQuery.Message.Chat, e.CallbackQuery.Message.MessageId);

                Guid guid = Guid.Parse(e.CallbackQuery.Data.AsSpan(e.CallbackQuery.Data.IndexOf('/') + 1));

                DeleteRequest(guid);

                await _telegramBotClient.AnswerCallbackQueryAsync(e.CallbackQuery.Id, $"Richiesta cancellata.");

                await ResponseRichiesteAsync(e.CallbackQuery.Message.Chat);
            }
            else if (e.CallbackQuery.Data.StartsWith("reverteDelete"))
            {
                await _telegramBotClient.DeleteMessageAsync(e.CallbackQuery.Message.Chat, e.CallbackQuery.Message.MessageId);

                await SendRequestAsync(e, Guid.Parse(e.CallbackQuery.Data.AsSpan(e.CallbackQuery.Data.IndexOf('/') + 1)));
            }
        }

        #endregion

        #region Gestione Task
        private void Inizializza()
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource = null;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _cancellationToken = _cancellationTokenSource.Token;

            LoadConfig();

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            _telegramBotClient = new TelegramBotClient(_configuration.Token ?? throw new InvalidOperationException("Missing telegram token."));
            _telegramBotClient.OnMessage += async (args, e) =>
            {
                try
                {
                    await ReponseMessageAsync(e);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message ?? ex.InnerException?.Message);
                }
            };
            _telegramBotClient.OnCallbackQuery += async (args, e) =>
            {
                try
                {
                    await ResponseCallbackAsync(e);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message ?? ex.InnerException?.Message);
                }
            };
            _telegramBotClient.StartReceiving(cancellationToken: _cancellationToken);
        }

        private void LoadConfig()
        {
            string configuration = null;
            if (File.Exists("config.json"))
            {
                configuration = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json"));
            }

            if (string.IsNullOrEmpty(configuration))
            {
                throw new InvalidOperationException("Missing configuration.");
            }

            _configuration = JsonConvert.DeserializeObject<Configuration>(configuration);

            _request = new List<ChartRequest>();

            if (File.Exists("requests.json"))
            {
                _request = JsonConvert.DeserializeObject<List<ChartRequest>>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "requests.json")));
            }

            Console.WriteLine($"Chart bot avviato. Ask to user id: {_configuration.AskToUserId}. Admins: {string.Join(", ", _configuration.Admins)}");
        }

        public void Stop()
        {
            _cancellationTokenSource.Cancel();
        }

        #endregion

        #region Dispose
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _cancellationTokenSource?.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
