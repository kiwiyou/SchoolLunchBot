using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using NLog;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
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

        private static IDictionary<int, LunchAlarm> Alarm;

        private static SchoolDatabase _database;

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
            Logger.Info("데이터베이스에 연결하는 중...");
            IDbConnection database = new MySqlConnection(
                ConfigurationManager.ConnectionStrings["Registry"].ConnectionString);
            try
            {
                database.Open();
                _database = new SchoolDatabase(database);
            }
            catch (Exception ex)
            {
                Logger.Fatal(ex, "데이터베이스에 연결 중 오류가 발생했습니다.");
                Console.WriteLine("아무 키나 누르면 종료됩니다...");
                Console.ReadKey(true);
                return;
            }
            Logger.Info("봇 정보를 받아오는 중...");
            var me = Client.GetMeAsync().Result;
            Console.Title = $"@{me.Username}";
            Logger.Info("구독 정보를 가져오는 중...");
            Alarm = _database.GetAlarms(Client);
            foreach (var alarm in Alarm)
            {
                alarm.Value.Enabled = true;
            }

            Client.OnMessage += BotMessageReceived;
            Client.OnCallbackQuery += BotCallbackChosen;

            Client.StartReceiving();
            Logger.Info("급식봇이 시작되었습니다. Q를 눌러 종료할 수 있습니다.");
            while (true)
            {
                if (char.ToLower(Console.ReadKey().KeyChar) == 'q')
                    break;
            }
            Client.StopReceiving();
        }

        private static async void BotCallbackChosen(object sender, CallbackQueryEventArgs e)
        {
            var chosen = e.CallbackQuery;
            if (UserStates.TryGetValue(chosen.From.Id, out UserState state) &&
                state == UserState.UseSavedSchoolRequested)
            {
                if (chosen.Data == "yes")
                {
                    await Client.DeleteMessageAsync(chosen.Message.Chat.Id, chosen.Message.MessageId);
                    await RequestYear(chosen.Message.Chat, chosen.From);
                }
                else
                {
                    await RequestSchoolName(chosen.Message.Chat);
                    UserStates[chosen.From.Id] = UserState.SchoolNameRequested;
                }
            }
            if (TempSchoolInfo.TryGetValue(chosen.Message.MessageId, out ICollection<SchoolInfo> schools))
            {
                if (chosen.Data == "select")
                {
                    var message = chosen.Message.Text;
                    var codePos = message.IndexOf("코드: ");
                    if (codePos < 0)
                    {
                        await Client.AnswerCallbackQueryAsync(chosen.Id, "학교를 선택해 주세요!");
                        return;
                    }
                    var code = message.Substring(codePos + 4);
                    var info = schools.First(school => school.Code == code);
                    TempSchoolInfo.Remove(chosen.Message.MessageId);
                    await Client.DeleteMessageAsync(chosen.Message.Chat.Id, chosen.Message.MessageId);
                    switch (UserStates[chosen.From.Id])
                    {
                        case UserState.SchoolNameSelect:
                            if (SelectedSchoolInfo.ContainsKey(chosen.From.Id))
                                SelectedSchoolInfo[chosen.From.Id] = info;
                            else
                                SelectedSchoolInfo.Add(chosen.From.Id, info);
                            await RequestYear(chosen.Message.Chat, chosen.From);
                            break;
                        case UserState.SaveSchoolSelect:
                            _database.SetSchool(chosen.From.Id, info);
                            await Client.AnswerCallbackQueryAsync(chosen.Id, "저장되었습니다!");
                            break;
                    }
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
                    await Client.AnswerCallbackQueryAsync(chosen.Id, "급식이 없습니다!");
                }
            }
        }

        private static async void BotMessageReceived(object sender, MessageEventArgs messageEventArgs)
        {
            try
            {
                var message = messageEventArgs.Message;
                if (message == null || message.Type != MessageType.TextMessage) return;
                if (DateTime.Now.Subtract(message.Date).TotalMinutes > 1) return;

                var chatId = message.Chat.Id;
                var fromId = message.From.Id;

                if (message.Text.StartsWith("/start"))
                {
                    Logger.Info("/start - User {0}", fromId);
                    await Client.SendTextMessageAsync(chatId, "`/급식` - 급식 정보를 검색합니다.\n" +
                                                              "`/저장` - 학교 정보를 저장합니다.\n" +
                                                              "`/구독` - 지정한 시각에 급식 정보를 발송합니다.\n" +
                                                              "`/구독해제` - 구독을 해제합니다.",
                        replyToMessageId: message.MessageId, parseMode: ParseMode.Markdown);
                }
                else if (message.Text.StartsWith("/급식"))
                {
                    Logger.Info("/급식 - User {0}", fromId);
                    var saved = _database.GetSchool(fromId);
                    if (saved != null)
                    {
                        if (SelectedSchoolInfo.ContainsKey(fromId))
                            SelectedSchoolInfo[fromId] = saved;
                        else SelectedSchoolInfo.Add(fromId, saved);
                        await RequestSavedSchool(message);
                    }
                    else
                    {
                        UserStates.Remove(fromId);
                        UserStates.Add(fromId, UserState.SchoolNameRequested);
                        await RequestSchoolName(message.Chat, message.MessageId);
                    }
                }
                else if (message.Text.StartsWith("/저장"))
                {
                    Logger.Info("/저장 - User {0}", fromId);
                    UserStates.Remove(fromId);
                    UserStates.Add(fromId, UserState.SaveSchoolRequested);
                    await RequestSchoolName(message.Chat, message.MessageId);
                }
                else if (message.Text.StartsWith("/구독"))
                {
                    Logger.Info("/구독 - User {0}", fromId);
                    UserStates.Remove(fromId);
                    UserStates.Add(fromId, UserState.SubscribeRequested);
                    await RequestSubscribe(message.Chat, message.MessageId);
                }
                else if (message.Text.StartsWith("/구독해제"))
                {
                    Logger.Info("/구독해제 - User {0}", fromId);
                    if (Alarm.TryGetValue(fromId, out LunchAlarm alarm))
                    {
                        alarm.Enabled = false;
                        Alarm.Remove(fromId);
                        _database.RemoveAlarm(fromId);
                        await Client.SendTextMessageAsync(chatId, "구독이 해제되었습니다.",
                            replyToMessageId: message.MessageId);
                    }
                    else
                    {
                        await Client.SendTextMessageAsync(chatId, "구독 상태가 아닙니다.",
                            replyToMessageId: message.MessageId);
                    }
                }
                else if (UserStates.TryGetValue(fromId, out UserState state))
                {
                    Logger.Debug("User {0}: \"{1}\"", fromId, message.Text);
                    switch (state)
                    {
                        case UserState.SchoolNameRequested:
                        {
                            var name = message.Text;
                            var candidates = await Schools.FindAsync(name);
                            await SendSchoolSelect(message.Chat, candidates, message.MessageId);
                            UserStates[fromId] = UserState.SchoolNameSelect;
                        }
                            break;
                        case UserState.SaveSchoolRequested:
                        {
                            var name = message.Text;
                            var candidates = await Schools.FindAsync(name);
                            await SendSchoolSelect(message.Chat, candidates, message.MessageId);
                            UserStates[fromId] = UserState.SaveSchoolSelect;
                        }
                            break;
                        case UserState.YearRequested:
                            try
                            {
                                var date = message.Text.Split('/');
                                var year = int.Parse(date[0]);
                                var month = int.Parse(date[1]);
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
                                var result = await Client.SendTextMessageAsync(chatId,
                                    $"{school.Name}의 {year}년 {month}월 급식입니다.",
                                    replyToMessageId: message.MessageId, replyMarkup: keyboard);
                                TempLunchInfo.Add(result.MessageId, lunch);
                                SelectedSchoolInfo.Remove(message.From.Id);
                                UserStates.Remove(message.From.Id);
                            }
                            catch (Exception ex)
                            {
                                if (ex is ArgumentOutOfRangeException || ex is IndexOutOfRangeException ||
                                    ex is FormatException)
                                {
                                    Logger.Info("Ignore message \"{0}\" with exception:", message.Text);
                                    Logger.Info(ex);
                                    await Client.SendTextMessageAsync(chatId, "잘못 입력하셨습니다.",
                                        replyToMessageId: message.MessageId);
                                    await RequestYear(message.Chat, message.From);
                                }
                                else throw;
                            }
                            break;
                        case UserState.SubscribeRequested:
                            try
                            {
                                var time = message.Text.Split(':');
                                var hour = int.Parse(time[0]);
                                var minute = int.Parse(time[1]);
                                if (Alarm.TryGetValue(fromId, out LunchAlarm alarm))
                                {
                                    alarm.Hour = hour;
                                    alarm.Minute = minute;
                                    _database.AddAlarm(alarm);
                                }
                                else
                                {
                                    var newAlarm = new LunchAlarm(Client, _database, fromId, chatId)
                                    {
                                        Enabled = true,
                                        Hour = hour,
                                        Minute = minute
                                    };
                                    Alarm.Add(fromId, newAlarm);
                                    _database.AddAlarm(newAlarm);
                                }
                                await Client.SendTextMessageAsync(chatId, $"매일 {hour}시 {minute}분에 급식 정보가 발송됩니다.",
                                    replyToMessageId: message.MessageId);
                                UserStates.Remove(fromId);
                            }
                            catch (Exception ex)
                            {
                                if (ex is ArgumentOutOfRangeException || ex is IndexOutOfRangeException ||
                                    ex is FormatException)
                                {
                                    Logger.Info("Ignore message \"{0}\" with exception:", message.Text);
                                    Logger.Info(ex);
                                    await Client.SendTextMessageAsync(chatId, "잘못 입력하셨습니다.",
                                        replyToMessageId: message.MessageId);
                                    await RequestSubscribe(message.Chat, message.MessageId);
                                    break;
                                }
                                throw;
                            }
                            break;
                    }
                }
            }
            catch (ApiRequestException telegramException)
            {
                Logger.Fatal("Error replying: {0}", telegramException.Message);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }

        private static async Task RequestSubscribe(Chat chat, int replyTo = 0)
        {
            await Client.SendTextMessageAsync(chat.Id, "급식 정보를 받을 시간을 입력해주세요.\n" +
                                                       "예) `12:20`",
                ParseMode.Markdown, replyToMessageId: replyTo);
        }

        private static async Task RequestSchoolName(Chat chat, int replyTo = 0)
        {
            await Client.SendTextMessageAsync(chat.Id, "학교명을 입력해주세요.",
                replyToMessageId: replyTo, replyMarkup: new ForceReply
                {
                    Force = true
                });
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

        private static async Task RequestSavedSchool(Message message)
        {
            var keyboard = new InlineKeyboardMarkup(new InlineKeyboardButton[]
            {
                new InlineKeyboardCallbackButton("예", "yes"),
                new InlineKeyboardCallbackButton("아니오", "no"), 
            });
            var school = SelectedSchoolInfo[message.From.Id];
            await Client.SendTextMessageAsync(message.Chat.Id, "저장된 학교 정보가 있습니다. 사용하시겠습니까?\n\n" +
                                                               $"학교명: {school.Name}\n" +
                                                               $"주소: {school.Address}",
                replyMarkup: keyboard);
            UserStates.Remove(message.From.Id);
            UserStates.Add(message.From.Id, UserState.UseSavedSchoolRequested);
        }

        private static async Task SendSchoolSelect(Chat chat, ICollection<SchoolInfo> schools, int replyTo = 0)
        {
            if (schools == null)
            {
                await Client.SendTextMessageAsync(chat.Id, "학교를 찾을 수 없습니다.",
                    replyToMessageId: replyTo);
            }
            else
            {
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
                var result = await Client.SendTextMessageAsync(chat.Id, $"총 {schools.Count}곳의 학교를 찾았습니다.",
                    replyToMessageId: replyTo, replyMarkup: keyboard);
                TempSchoolInfo.Add(result.MessageId, schools);
            }
        }
    }

    public enum UserState
    {
        SchoolNameRequested,
        YearRequested,
        UseSavedSchoolRequested,
        SaveSchoolRequested,
        SchoolNameSelect,
        SaveSchoolSelect,
        SubscribeRequested
    }
}