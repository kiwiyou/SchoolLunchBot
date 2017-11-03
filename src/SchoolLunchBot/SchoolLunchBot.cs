using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineKeyboardButtons;
using Telegram.Bot.Types.ReplyMarkups;

namespace SchoolLunch.Bot
{
    internal sealed class SchoolLunchBot
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        private static readonly ITelegramBotClient Client =
            new TelegramBotClient(ConfigurationManager.AppSettings["BotToken"]);

        private static readonly Dictionary<int, UserState> UserStates = new Dictionary<int, UserState>();

        private static readonly Dictionary<int, ICollection<SchoolInfo>> TempSchoolInfo =
            new Dictionary<int, ICollection<SchoolInfo>>();

        private static readonly Dictionary<int, SchoolInfo> SelectedSchoolInfo = new Dictionary<int, SchoolInfo>();

        private static readonly Dictionary<int, IDictionary<int, string>> TempLunchInfo =
            new Dictionary<int, IDictionary<int, string>>();

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
            Client.OnCallbackQuery += BotCallbackChosen;

            Client.StartReceiving();
            Logger.Info("급식봇이 시작되었습니다. Q를 눌러 종료할 수 있습니다.");
            while (char.ToLower(Console.ReadKey().KeyChar) != 'q')
            {
            }
            Client.StopReceiving();
        }

        private static async void BotCallbackChosen(object sender, CallbackQueryEventArgs e)
        {
            var chosen = e.CallbackQuery;
            if (TempSchoolInfo.TryGetValue(chosen.Message.MessageId, out ICollection<SchoolInfo> schools))
            {
                if (chosen.Data == "select")
                {
                    var message = chosen.Message.Text;
                    var codePos = message.IndexOf("코드: ");
                    if (codePos < 0)
                    {
                        await Client.AnswerCallbackQueryAsync(chosen.Id, "학교를 선택해 주세요!", true);
                        return;
                    }
                    var code = message.Substring(codePos + 4);
                    var info = schools.First(school => school.Code == code);
                    TempSchoolInfo.Remove(chosen.Message.MessageId);
                    SelectedSchoolInfo.Add(chosen.From.Id, info);
                    await Client.DeleteMessageAsync(chosen.Message.Chat.Id, chosen.Message.MessageId);
                    await RequestYear(chosen.Message.Chat, chosen.From);
                } else if (chosen.Data == "cancel")
                {
                    TempSchoolInfo.Remove(chosen.Message.MessageId);
                    await Client.DeleteMessageAsync(chosen.Message.Chat.Id, chosen.Message.MessageId);
                    UserStates.Remove(chosen.From.Id);
                }
                else
                {
                    var index = Convert.ToInt32(chosen.Data);
                    var selected = schools.ElementAt(index);
                    var result = $"학교명: {selected.Name}\n" +
                                 $"주소: {selected.Address}\n" +
                                 $"코드: {selected.Code}";
                    if (result == chosen.Message.Text) return;
                    var keyboard = new InlineKeyboardMarkup(new[]
                    {
                        Enumerable.Range(1, schools.Count)
                            .Select(i => new InlineKeyboardCallbackButton(i.ToString(), (i - 1).ToString()))
                            .Cast<InlineKeyboardButton>()
                            .ToArray(),
                        new InlineKeyboardButton[]
                        {
                            new InlineKeyboardCallbackButton("선택", "select"),
                            new InlineKeyboardCallbackButton("취소", "cancel"),
                        }
                    });
                    await Client.EditMessageTextAsync(chosen.Message.Chat.Id, chosen.Message.MessageId,
                        result, ParseMode.Markdown, replyMarkup: keyboard);
                }
            } else if (TempLunchInfo.TryGetValue(chosen.Message.MessageId, out IDictionary<int, string> lunch))
            {
                var data = chosen.Data.Split('/');
                var day = Convert.ToInt32(data[0]);
                var days = Convert.ToInt32(data[1]);
                if (lunch.TryGetValue(day, out string menu))
                {
                    var change = menu;
                    if (chosen.Message.Text == change) return;
                    var keyboard = new InlineKeyboardMarkup(Enumerable
                        .Range(1, days)
                        .Select(d => new InlineKeyboardCallbackButton(d.ToString(), d.ToString() + "/" + days))
                        .Cast<InlineKeyboardButton>()
                        .ToArray()
                        .Split(7));
                    await Client.EditMessageTextAsync(chosen.Message.Chat.Id, chosen.Message.MessageId,
                        change, replyMarkup: keyboard);
                }
                else
                {
                    await Client.AnswerCallbackQueryAsync(chosen.Id, "급식이 없습니다!", true);
                }
            }
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
                await Client.SendTextMessageAsync(chatId, "`/급식` - 급식 정보를 검색합니다.",
                    replyToMessageId: message.MessageId, parseMode: ParseMode.Markdown);
            } else if (message.Text.StartsWith("/급식"))
            {
                Logger.Info("/급식 - User {0}", fromId);
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
                            var keyboard = new InlineKeyboardMarkup(new[]
                            {
                                Enumerable.Range(1, candidates.Count)
                                    .Select(i => new InlineKeyboardCallbackButton(i.ToString(), (i - 1).ToString()))
                                    .Cast<InlineKeyboardButton>()
                                    .ToArray(),
                                new InlineKeyboardButton[]
                                {
                                    new InlineKeyboardCallbackButton("선택", "select"),
                                    new InlineKeyboardCallbackButton("취소", "cancel"), 
                                }
                            });
                            var result = await Client.SendTextMessageAsync(chatId, $"총 {candidates.Count}곳의 학교를 찾았습니다.",
                                replyToMessageId: message.MessageId, replyMarkup: keyboard);
                            TempSchoolInfo.Add(result.MessageId, candidates);
                        }
                        UserStates.Remove(fromId);
                        break;
                    case UserState.YearRequested:
                        try
                        {
                            var date = message.Text.Split('/');
                            var year = Convert.ToInt32(date[0]);
                            var month = Convert.ToInt32(date[1]);
                            var school = SelectedSchoolInfo[message.From.Id];
                            var days = DateTime.DaysInMonth(year, month);
                            var lunch = await school.GetLunch(year, month);
                            var keyboard = new InlineKeyboardMarkup(Enumerable
                                .Range(1, days)
                                .Select(day => new InlineKeyboardCallbackButton(day.ToString(),
                                    day.ToString() + "/" + days.ToString()))
                                .Cast<InlineKeyboardButton>()
                                .ToArray()
                                .Split(7));
                            await Client.SendChatActionAsync(chatId, ChatAction.Typing);
                            var result = await Client.SendTextMessageAsync(chatId,
                                $"{school.Name}의 {year}년 {month}월 급식입니다.",
                                replyToMessageId: message.MessageId, replyMarkup: keyboard);
                            TempLunchInfo.Add(result.MessageId, lunch);
                        } catch (ArgumentOutOfRangeException)
                        {
                            await Client.SendChatActionAsync(chatId, ChatAction.Typing);
                            await Client.SendTextMessageAsync(chatId, "잘못 입력하셨습니다.",
                                replyToMessageId: message.MessageId);
                            await RequestYear(message.Chat, message.From);
                        }
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

        private static async Task RequestYear(Chat chat, User from)
        {
            await Client.SendChatActionAsync(chat.Id, ChatAction.Typing);
            await Client.SendTextMessageAsync(chat.Id, "급식을 찾을 연도와 달을 입력해주세요.\n" +
                                                       "예) `2017/09`",
                ParseMode.Markdown, replyMarkup: new ForceReply
                {
                    Force = true
                });
            UserStates[from.Id] = UserState.YearRequested;
        }
    }

    public enum UserState
    {
        SchoolNameRequested,
        YearRequested
    }
}