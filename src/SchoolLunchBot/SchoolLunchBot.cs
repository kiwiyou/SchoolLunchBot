using System;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;
using NLog;
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

        private static readonly Dictionary<int, UserState> UserStates = new Dictionary<int, UserState>();

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
                Logger.Info("/start - User {0}", fromId);
                await Client.SendChatActionAsync(chatId, ChatAction.Typing);
                await Client.SendTextMessageAsync(chatId, "급식을 검색할 수 있는 봇입니다!" +
                                                                   "`/help`를 쳐보세요!",
                    replyToMessageId: message.MessageId, parseMode: ParseMode.Markdown);
            } else if (message.Text.StartsWith("/help"))
            {
                Logger.Info("/help - User {0}", fromId);
                await Client.SendChatActionAsync(chatId, ChatAction.Typing);
                await Client.SendTextMessageAsync(chatId, "`/학교검색` - 학교 코드를 검색합니다.",
                    replyToMessageId: message.MessageId, parseMode: ParseMode.Markdown);
            } else if (message.Text.StartsWith("/취소"))
            {
                if (!UserStates.Remove(fromId)) return;
                Logger.Info("/취소 - User {0}", fromId);
                await Client.SendChatActionAsync(chatId, ChatAction.Typing);
                await Client.SendTextMessageAsync(chatId, "작업이 취소되었습니다.",
                    replyToMessageId: message.MessageId);
            } else if (message.Text.StartsWith("/학교검색"))
            {
                Logger.Info("/학교검색 - User {0}", fromId);
                await RequestSchoolName(message);
            }
            else if (UserStates.TryGetValue(fromId, out UserState state))
            {
                switch (state)
                {
                    case UserState.SchoolNameRequested:
                        var name = message.Text;
                        var candidates = await Schools.FindAsync(name);
                        await Client.SendChatActionAsync(chatId, ChatAction.Typing);
                        if (candidates == null)
                        {
                            await Client.SendTextMessageAsync(chatId, "학교를 찾을 수 없습니다.",
                                replyToMessageId: message.MessageId);
                        }
                        else
                        {
                            await Client.SendTextMessageAsync(chatId, $"총 {candidates.Count}곳의 학교를 찾았습니다.",
                                replyToMessageId: message.MessageId);
                            foreach (var candidate in candidates)
                            {
                                await Client.SendTextMessageAsync(chatId,
                                    $"학교명: `{candidate.Name}`\n" +
                                    $"주소: `{candidate.Address}`\n" +
                                    $"코드: `{candidate.Code}`",
                                    ParseMode.Markdown);
                            }
                        }
                        UserStates.Remove(fromId);
                        break;
                }
            }
        }

        private static async Task RequestSchoolName(Message message)
        {
            await Client.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);
            await Client.SendTextMessageAsync(message.Chat.Id, "학교명을 입력해주세요.",
                replyToMessageId: message.MessageId, replyMarkup: new ForceReply
                {
                    Force = true
                });
            UserStates[message.From.Id] = UserState.SchoolNameRequested;
        }
    }

    public enum UserState
    {
        SchoolNameRequested
    }
}