using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using SchoolFinder;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace SchoolLunch.Bot
{
    internal sealed class SchoolLunchBot
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        private static readonly ITelegramBotClient Client =
            new TelegramBotClient(ConfigurationManager.AppSettings["BotToken"]);

        private static readonly SchoolSearch SchoolFinder = new SchoolSearch();

        private static readonly Dictionary<int, UserState> UserStates = new Dictionary<int, UserState>();
        private static readonly Dictionary<int, Regions> TempUserRegion = new Dictionary<int, Regions>();

        private static void Main(string[] args)
        {
            Run();
        }

        private static void Run()
        {
            Console.WriteLine("   __                  _       ___       _   \n" +
                              "  / / _   _ _ __   ___| |__   / __\\ ___ | |_ \n" + 
                              " / / | | | | '_ \\ / __| '_ \\ /__\\/// _ \\| __|\n" +
                              "/ /__| |_| | | | | (__| | | / \\/  \\ (_) | |_ \n" +
                              "\\____/\\__,_|_| |_|\\___|_| |_\\_____/\\___/ \\__|\n" +
                              "                                             ");
            Logger.Info("봇 정보를 받아오는 중...");
            var me = Client.GetMeAsync().Result;
            Console.Title = $"@{me.Username}";

            Client.OnMessage += BotMessageReceived;

            Client.StartReceiving();
            Logger.Info("급식봇이 시작되었습니다. Q를 눌러 종료할 수 있습니다.");
            while (char.ToLower(Console.ReadKey().KeyChar) != 'q')
            {
            }
            Client.StopReceiving();
        }
        
        private static async void BotMessageReceived(object sender, MessageEventArgs messageEventArgs)
        {
            var message = messageEventArgs.Message;
            if (message == null || message.Type != MessageType.TextMessage) return;

            var chatId = message.Chat.Id;
            var fromId = message.From.Id;

            if (message.Text.StartsWith("/start"))
            {
                await Client.SendChatActionAsync(chatId, ChatAction.Typing);
                await Client.SendTextMessageAsync(chatId, "급식을 검색할 수 있는 봇입니다!" +
                                                                   "`/help`를 쳐보세요!",
                    replyToMessageId: message.MessageId, parseMode: ParseMode.Markdown);

                Logger.Info("/start - User {0}", fromId);
            } else if (message.Text.StartsWith("/help"))
            {
                await Client.SendChatActionAsync(chatId, ChatAction.Typing);
                await Client.SendTextMessageAsync(chatId, "`/학교검색` - 학교 코드를 검색합니다.",
                    replyToMessageId: message.MessageId, parseMode: ParseMode.Markdown);
                Logger.Info("/help - User {0}", fromId);
            } else if (message.Text.StartsWith("/학교검색"))
            {
                await RespondSchoolQuery(message);
                Logger.Info("/학교검색 - User {0}", fromId);
            }
            else if (UserStates.TryGetValue(fromId, out UserState state))
            {
                switch (state)
                {
                    case UserState.RegionRequested:
                        try
                        {
                            var region = SchoolUtil.GetRegion(message.Text);
                            TempUserRegion.Add(fromId, region);
                            await RequestSchoolName(message);
                        }
                        catch (KeyNotFoundException)
                        {
                            await RespondSchoolQuery(message);
                        }
                        break;
                    case UserState.SchoolNameRequested:
                        try
                        {
                            var name = message.Text;
                            var type = SchoolUtil.GetSchoolType(name);
                            var candidates = SchoolFinder.SearchSchool(type, TempUserRegion[fromId], name);
                            await Client.SendChatActionAsync(chatId, ChatAction.Typing);
                            await Client.SendTextMessageAsync(chatId, $"총 {candidates.Count}곳의 학교를 찾았습니다.",
                                replyToMessageId: message.MessageId);
                            foreach (var candidate in candidates)
                            {
                                await Client.SendTextMessageAsync(chatId,
                                    $"`학교명: {candidate.Name}`\n`주소: {candidate.Adress}`\n`코드: {candidate.Code}`",
                                    ParseMode.Markdown);
                            }
                            UserStates.Remove(fromId);
                        }
                        catch (ArgumentException)
                        {
                            await Client.SendChatActionAsync(chatId, ChatAction.Typing);
                            await Client.SendTextMessageAsync(chatId, "잘못된 학교명입니다.",
                                replyToMessageId: message.MessageId);
                            await RequestSchoolName(message);
                        }
                        break;
                }
            }
        }

        private static async Task RespondSchoolQuery(Message message)
        {
            var chatId = message.Chat.Id;
            var fromId = message.From.Id;
            await Client.SendChatActionAsync(chatId, ChatAction.Typing);
            var regionKeyboard = new ReplyKeyboardMarkup(Enum.GetValues(typeof(Regions)).Cast<Regions>()
                .Select(region => new KeyboardButton(SchoolUtil.GetRegionName(region))).ToArray())
            {
                OneTimeKeyboard = true,
                ResizeKeyboard = true
            };
            await Client.SendTextMessageAsync(chatId, "지역을 선택해주세요.", replyToMessageId: message.MessageId,
                replyMarkup: regionKeyboard);
            
            if (UserStates.ContainsKey(fromId))
            {
                UserStates[fromId] = UserState.RegionRequested;
                TempUserRegion.Remove(fromId);
            }
            else
                UserStates.Add(fromId, UserState.RegionRequested);
        }

        private static async Task RequestSchoolName(Message message)
        {
            await Client.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);
            await Client.SendTextMessageAsync(message.Chat.Id, "학교명을 입력해주세요.",
                replyToMessageId: message.MessageId, replyMarkup: new ForceReply());
            UserStates[message.From.Id] = UserState.SchoolNameRequested;
        }
    }

    public enum UserState
    {
        RegionRequested,
        SchoolNameRequested,
    }
}