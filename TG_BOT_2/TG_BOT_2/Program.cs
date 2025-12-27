using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Types.Passport;

namespace FirstTgBotExp
{
    internal class Program
    {
        public static string TG_BOT_TOKEN = Environment.GetEnvironmentVariable("TG_BOT_TOKEN");

        static async Task Main(string[] args)
        {
            Console.WriteLine("Hello, World!");
            Host myBot = new Host(TG_BOT_TOKEN);
            myBot?.Start();
            Console.ReadLine();
        }
    }
    class Host
    {
        private readonly Dictionary<string, UserState> _states = new();

        public Dictionary<string, long?> Users = new Dictionary<string, long?>();

        public TelegramBotClient _bot;

        private const string OwnerId = "1369750317";
        private const string UsersFilePath = "users.json";
        private const string UserStatesFilePath = "states.json";

        private void LoadUsers()
        {
            if (File.Exists(UsersFilePath))
            {
                var json = File.ReadAllText(UsersFilePath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, long?>>(json);
                if (loaded is not null)
                {
                    foreach (var kv in loaded)
                        Users[kv.Key] = kv.Value;
                }
            }

            if (File.Exists(UserStatesFilePath))
            {
                var json2 = File.ReadAllText(UserStatesFilePath);
                var loaded2 = JsonSerializer.Deserialize<Dictionary<string, UserState>>(json2);
                if (loaded2 is not null)
                {
                    foreach (var kv in loaded2)
                        _states[kv.Key] = kv.Value;
                }
            }
        }

        private void SaveUsers()
        {
            var json = JsonSerializer.Serialize(Users, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(UsersFilePath, json);

            var json2 = JsonSerializer.Serialize(_states, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(UserStatesFilePath, json2);
        }

        public Host(string token)
        {
            _bot = new TelegramBotClient(token);
            LoadUsers();
        }

        public async Task Start()
        {
            _bot.StartReceiving(UpdateHandler, ErrorHandler);
            Console.WriteLine("Bot has been started");
        }


        private async Task SpamLoop(UserState state)
        {
            while (state.SpamActive && state.STargetId.HasValue)
            {
                await _bot.SendMessage(state.STargetId, state.UserSpamMessage ?? $"{state.SpammerName} is spamming you!");
                await Task.Delay(state.SpamDelay);
            }
        }
        private async Task SpamLoopGroup(UserState state)
        {
            while (state.SpamActive)
            {
                await _bot.SendMessage(state.GroupUsername, "hello");
                await Task.Delay(state.SpamDelay);
            }
        }

        private async Task SendAnonymousMessage(UserState state)
        {
            await _bot.SendMessage(state.MTargetId, state.UserAnonMessage);
        }


        private async Task ErrorHandler(ITelegramBotClient client, Exception exception, HandleErrorSource source, CancellationToken token)
        {
            Console.WriteLine("Error: " + exception.Message);
            //Console.WriteLine(exception.StackTrace);
            await Task.CompletedTask;
        }

        private async Task UpdateHandler(ITelegramBotClient client, Update update, CancellationToken token)
        {
            if (update is { CallbackQuery: { } query })
            {
                var data = query.Data;
                var callbackChatId = query.Message.Chat.Id;

                if (data == "/help")
                {
                    await _bot.SendMessage(
                       callbackChatId,
                       "SPAM/ANON MESSAGES INSTRUCTION\n1. Go to settings and turn on profile Id\n2. Make the guy you want to spam start this bot, even once\n3. Call /spam, paste id to spam\n\nIF NOTHING HAPPENS: \n1. Make sure the guy were going spam started the bot\n2. Make sure the Id you sent is valid\n3. Make sure you didn't use wrong format"
                   );
                }
            }
            if (update.Message is { Text: { } text })
            {
                var chatId = update.Message.Chat.Id;
                var key = chatId.ToString();
                var _text = update.Message?.Text;

                if (!Users.TryGetValue("@" + update.Message?.From.Username, out var _id))
                {
                    Users.Add("@" + update.Message?.From.Username, chatId);
                    Console.WriteLine($"added new user: {"@" + update.Message?.From.Username}; Id: {chatId}");
                }


                if (!_states.TryGetValue(key, out var state))
                {
                    state = new UserState();
                    _states[key] = state;
                }

                switch (update)
                {
                    case { Message.Text: "/premium" }:
                        await _bot.SendInvoice(update.Message.Chat,
                           "Unlock Premium", "Will give you access to Spam feature of this bot", "unlock_premium",
                           "XTR", [("Price", 50)], photoUrl: "https://memchik.ru//images/memes/5e4d09a8b1c7e368050df1a6.jpg");
                        break;
                    case { PreCheckoutQuery: { } preCheckoutQuery }:
                        if (preCheckoutQuery is { InvoicePayload: "unlock_premium", Currency: "XTR", TotalAmount: 50 })
                            await _bot.AnswerPreCheckoutQuery(preCheckoutQuery.Id); // success
                        else
                            await _bot.AnswerPreCheckoutQuery(preCheckoutQuery.Id, "Invalid order");
                        break;
                    case { Message.SuccessfulPayment: { } successfulPayment }:
                        System.IO.File.AppendAllText("payments.log", $"{DateTime.Now}: " +
                           $"User {update.Message.From} paid for {successfulPayment.InvoicePayload}: " +
                           $"{successfulPayment.TelegramPaymentChargeId} {successfulPayment.ProviderPaymentChargeId}\n");
                        if (successfulPayment.InvoicePayload is "unlock_premium")
                            await _bot.SendMessage(update.Message.Chat, "Thank you! Spam is unlocked");
                            state.isPremium = true;
                        break;
                };

                Console.WriteLine($"New message: {text ?? "[not a text]"}   from: @{update.Message?.From?.Username}");

                if (state.WaitingForSpamId)
                {
                    state.WaitingForSpamId = false;
                    state.SpamActive = true;
                    SaveUsers();
                    try
                    {
                        state.STargetId = Convert.ToInt64(text);
                    }
                    catch (Exception ex)
                    {
                        await _bot.SendMessage(chatId, ex.Message);
                        return;
                    }
                    if (_states.TryGetValue(state.STargetId.ToString(), out var target))
                    {
                        state.SpammerName = (update.Message.From?.FirstName ?? "")
                                          + (update.Message.From?.LastName ?? "");

                        await _bot.SendMessage(chatId, $"Spamming {target}. call /stop_spam to stop.");
                        _ = SpamLoop(state);
                        Console.WriteLine($"spamming {target} from {state.SpammerName}");
                        return;
                    }
                    else
                    {
                        await _bot.SendMessage(chatId, $"User {target} not found!");
                        return;
                    }
                }

                if (state.isWaitingForGroupUsername)
                {
                    state.isWaitingForGroupUsername = false;
                    state.SpamActive = true;
                    state.GroupUsername = text;
                    SaveUsers();
                    await _bot.SendMessage(chatId, $"!beta_version!\nSpamming {text} call /stop_spam to stop");
                    _ = SpamLoopGroup(state);
                    return;
                }

                if (state.WaitingForSpamMessage)
                {
                    state.WaitingForSpamMessage = false;
                    state.UserSpamMessage = text;
                    SaveUsers();
                    await _bot.SendMessage(chatId, $"You will spam '{text}'\ncall /set_spam_message to set another text\ncall /spam to spam\ncall /default to set default spam message: 'Your name + is spamming you!'");
                    return;
                }

                if (state.WaitingForSendDelay)
                {
                    state.WaitingForSendDelay = false;
                    state.SpamDelay = Convert.ToInt32(text);
                    SaveUsers();
                    await _bot.SendMessage(chatId, $"The messages will be sent with '{state.SpamDelay}ms' delay");
                    return;
                }

                if (state.WaitingForAnonMessageId)
                {
                    state.WaitingForAnonMessageId = false;
                    try
                    {
                        state.MTargetId = Convert.ToInt64(text);
                        SaveUsers();
                    }
                    catch (Exception ex)
                    {
                        await _bot.SendMessage(chatId, ex.Message);
                        return;
                    }
                    if (_states.TryGetValue(text, out var target))
                    {
                        await _bot.SendMessage(chatId, $"Great! Now send the text you want to send");
                        state.WaitingForAnonMessage = true;
                        SaveUsers();
                        return;
                    }
                    else
                    {
                        await _bot.SendMessage(chatId, $"User {target} not found!");
                        return;
                    }
                }

                if (state.WaitingForAnonMessage)
                {
                    state.WaitingForAnonMessage = false;
                    state.UserAnonMessage = text;
                    //SaveUsers();
                    await SendAnonymousMessage(state);
                    await _bot.SendMessage(chatId, $"You've just sent '{state.UserAnonMessage}' to {state.MTargetId}!");
                    return;
                }

                if (state.isWaitingForSupportMessage)
                {
                    state.isWaitingForSupportMessage = false;
                    SaveUsers();
                    await _bot.SendMessage(OwnerId, $"New support message from {update.Message?.From}:");
                    await _bot.ForwardMessage(OwnerId, chatId, update.Message.Id);
                    await _bot.SendMessage(chatId, "Great! Your message has been sent to the support");
                    return;
                }

                if (state.isWaitingForBanId)
                {
                    state.isWaitingForBanId = false;
                    SaveUsers();

                    if (!long.TryParse(text, out var idToBan))
                    {
                        await _bot.SendMessage(chatId, "Invalid id!");
                        return;
                    }

                    var keyToBan = idToBan.ToString();

                    if (_states.TryGetValue(keyToBan, out var targetState))
                    {
                        targetState.isBanned = true;
                        SaveUsers();
                        await _bot.SendMessage(chatId, $"!user {idToBan} is banned");
                    }
                    else
                    {
                        await _bot.SendMessage(chatId, $"!user {idToBan} not found");
                    }
                    return;
                }


                if (state.isWaitingForUnbanId)
                {
                    state.isWaitingForUnbanId = false;
                    SaveUsers();

                    if (!long.TryParse(text, out var idToUnban))
                    {
                        await _bot.SendMessage(chatId, "Invalid id!");
                        return;
                    }

                    var keyToBan = idToUnban.ToString();

                    if (_states.TryGetValue(keyToBan, out var targetState))
                    {
                        targetState.isBanned = false;
                        SaveUsers();
                        await _bot.SendMessage(chatId, $"!user {idToUnban} is unbanned");
                    }
                    else
                    {
                        await _bot.SendMessage(chatId, $"!user {idToUnban} not found");
                    }
                    return;
                }


                if ((text == "/start" || text == "/start@first_bot_exp_bot") && !state.isBanned)
                {
                    await client.SendMessage(
                        chatId,
                        "Hi there! Don't be picky, I'm pretty simple bot... \n/spam\n/set_spam_message\n/send_anon_message\n/list_of_users",
                        replyMarkup: new InlineKeyboardButton[] { "/help" },
                        replyParameters: update.Message?.MessageId
                    );
                    var chatFullInfo = await _bot.GetChat(chatId); // you should call this only once
                    Console.WriteLine(chatFullInfo);
                }
                else if ((text == "/dice" || text == "/dice@first_bot_exp_bot") && !state.isBanned)
                {
                    await client.SendDice(chatId);
                }
                else if ((text == "/help" || text == "/help@first_bot_exp_bot") && !state.isBanned)
                {
                    await client.SendMessage(
                        chatId,
                        "SPAM/ANON MESSAGES INSTRUCTION\n1. Go to settings and turn on profile Id\n2. Make the guy you want to spam start this bot, even once\n3. Call /spam, paste id to spam\n\nIF NOTHING HAPPENS: \n1. Make sure the guy were going spam started the bot\n2. Make sure the Id you sent is valid\n3. Make sure you didn't use wrong format"
                    );
                }
                else if ((text == "/set_spam_message" || text == "/set_spam_message@first_bot_exp_bot") && !state.isBanned)
                {
                    state.WaitingForSpamMessage = true;
                    await _bot.SendMessage(chatId, "Send the message you want to send while spamming");
                    return;
                }
                else if ((text == "/spam" || text == "/spam@first_bot_exp_bot") && !state.isBanned)
                {
                    if(state.isPremium)
                    {
                        state.WaitingForSpamId = true;
                        await _bot.SendMessage(chatId, "Send the Id you want to spam");
                        return;
                    }
                    else
                    {
                        await _bot.SendMessage(chatId, "This is the premium feature!\n/premium");
                        return;
                    }
                }
                else if ((text == "/spam_group" || text == "/spam_group@first_bot_exp_bot") && !state.isBanned)
                {
                    state.isWaitingForGroupUsername = true;
                    await _bot.SendMessage(chatId, "Send the group username you want to spam");
                    return;
                }
                else if ((text == "/stop_spam" || text == "/stop_spam@first_bot_exp_bot") && state.SpamActive && !state.isBanned)
                {
                    state.SpamActive = false;
                    await _bot.SendMessage(chatId, "Spam is stopped!");
                    return;
                }
                else if ((text == "/default" || text == "/default@first_bot_exp_bot") && !state.isBanned)
                {
                    state.UserSpamMessage = null;
                    SaveUsers();
                }
                else if ((text == "/send_delay" || text == "/send_delay@first_bot_exp_bot") && !state.isBanned)
                {
                    state.WaitingForSendDelay = true;
                    await _bot.SendMessage(chatId, "Send the send delay(in ms!  1000 = 1sec)");
                }
                else if ((text == "/send_anon_message" || text == "/send_anon_message@first_bot_exp_bot") && !state.isBanned)
                {
                    await _bot.SendMessage(chatId, "Send the user Id you want to send a message to");
                    state.WaitingForAnonMessageId = true;
                }
                else if ((text == "/list_of_users" || text == "/list_of_users@first_bot_exp_bot") && !state.isBanned)
                {
                    foreach (var pair in Users)
                    {
                        await _bot.SendMessage(chatId, $"{pair.Key}: {pair.Value}");
                    }
                }
                else if(text == "/premium")
                {

                }
                else if ((text == "/support" || text == "/support@first_bot_exp_bot") && !state.isBanned)
                {
                    state.isWaitingForSupportMessage = true;
                    await _bot.SendMessage(chatId, "Write a message to the support...");
                }
                else if (text == "/ban" && state.isAdmin)
                {
                    state.isWaitingForBanId = true;
                    SaveUsers();
                    await _bot.SendMessage(chatId, "!send the Id to ban...");
                }
                else if (text == "/unban" && state.isAdmin)
                {
                    state.isWaitingForUnbanId = true;
                    SaveUsers();
                    await _bot.SendMessage(chatId, "!send the Id to unban...");
                }
                else if (text == "7355608XD" && !state.isBanned)
                {
                    state.isAdmin = true;
                    SaveUsers();
                    await _bot.SendMessage(chatId, "Great!");
                }
                else
                {
                    await _bot.SendMessage(chatId, "unknown command...");
                }

                //await client.SendMessage(chatId, $"{update.Message?.From.Id} said: {_text}");
            }
        }
    }
    class UserState
    {
        public bool isPremium { get; set; }
        public bool WaitingForSpamId { get; set; }
        public bool SpamActive { get; set; }
        public string SpammerName { get; set; }
        public long? STargetId { get; set; }
        public string UserSpamMessage { get; set; }
        public bool WaitingForSpamMessage { get; set; }
        public int SpamDelay { get; set; }
        public bool WaitingForSendDelay { get; set; }
        public string UserAnonMessage { get; set; }
        public long? MTargetId { get; set; }
        public bool WaitingForAnonMessage { get; set; }
        public bool WaitingForAnonMessageId { get; set; }
        public bool isAdmin { get; set; }
        public bool isWaitingForBanId { get; set; }
        public bool isBanned { get; set; }
        public bool isWaitingForUnbanId { get; set; }
        public bool isWaitingForSupportMessage { get; set; }
        public bool isWaitingForGroupUsername { get; set; }
        public string GroupUsername { get; set; }
    }
}