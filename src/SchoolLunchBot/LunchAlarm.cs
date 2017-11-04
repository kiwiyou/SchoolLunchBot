using System;
using System.Timers;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;

namespace SchoolLunch.Bot
{
    internal class LunchAlarm
    {
        private readonly ITelegramBotClient _bot;
        private readonly SchoolDatabase _database;
        private readonly Timer _timer;

        public LunchAlarm(ITelegramBotClient bot, SchoolDatabase db, int user, long chat)
        {
            _bot = bot;
            _database = db;
            UserId = user;
            ChatId = chat;
            _timer = new Timer(1000);
            _timer.Elapsed += TimerElapsed;
        }

        private async void TimerElapsed(object sender, ElapsedEventArgs e)
        {
            var now = DateTime.Now;
            if (now.Second != 0 || now.Minute != Minute || now.Hour != Hour) return;
            var school = _database.GetSchool(UserId);
            try
            {
                if (school == null)
                {
                    await _bot.SendTextMessageAsync(ChatId, "`/저장` 명령어로 먼저 학교를 저장해주세요!", ParseMode.Markdown);
                    return;
                }
                var lunches = await school.GetLunch(now.Year, now.Month);
                if (lunches.TryGetValue(now.Day, out string lunch))
                {
                    await _bot.SendTextMessageAsync(ChatId, lunch);
                }
                else
                {
                    await _bot.SendTextMessageAsync(ChatId, "급식이 없습니다.");
                }
            }
            catch (ApiRequestException)
            {
                Enabled = false;
            }
        }

        private int _hour;
        public int Hour
        {
            get => _hour;
            set
            {
                if (value < 0 || value > 23)
                    throw new ArgumentOutOfRangeException(nameof(value), 0, "Illegal value for hour.");
                _hour = value;
            }
        }

        private int _minute;
        public int Minute
        {
            get => _minute;
            set
            {
                if (value < 0 || value > 59)
                    throw new ArgumentOutOfRangeException(nameof(value), 0, "Illegal value for minute.");
                _minute = value;
            }
        }

        public int UserId { get; }
        public long ChatId { get; }

        public bool Enabled
        {
            get => _timer.Enabled;
            set => _timer.Enabled = value;
        }
    }
}
