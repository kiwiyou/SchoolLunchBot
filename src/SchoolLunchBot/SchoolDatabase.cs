using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using Telegram.Bot;

namespace SchoolLunch.Bot
{
    internal class SchoolDatabase
    {
        private readonly IDbConnection _database;

        public SchoolDatabase(IDbConnection connection)
        {
            _database = connection;
        }

        private const string GetSchoolQuery =
            "SELECT school.* FROM Users AS user " +
            "INNER JOIN Schools AS school ON user.Code = school.Code " +
            "WHERE user.Id = @Id LIMIT 1";

        public SchoolInfo GetSchool(int id)
        {
            var schools = _database.Query<SchoolInfo>(GetSchoolQuery, new {Id = id});
            return schools.FirstOrDefault();
        }

        private const string AddUserQuery =
            "INSERT INTO Users (Id, Code) VALUES (@Id, @Code) " +
            "ON DUPLICATE KEY UPDATE Code = @Code";

        private const string AddSchoolQuery =
            "INSERT IGNORE INTO Schools (Code, Name, Type, Address, Office) " +
            "VALUES (@Code, @Name, @Type, @Address, @Office)";

        public void SetSchool(int id, SchoolInfo school)
        {
            _database.Execute(AddUserQuery, new {Id = id, Code = school.Code});
            _database.Execute(AddSchoolQuery, school);
        }

        private const string GetAlarmsQuery = "SELECT * FROM Alarms";

        public IDictionary<int, LunchAlarm> GetAlarms(ITelegramBotClient bot)
        {
            return _database.Query<AlarmData>(GetAlarmsQuery)
                .ToDictionary(raw => raw.Id, raw => new LunchAlarm(bot, this, raw.Id, raw.ChatId)
                {
                    Hour = raw.Hour,
                    Minute = raw.Minute
                });
        }

        private const string AddAlarmQuery =
            "INSERT INTO Alarms (Id, Hour, Minute, ChatId) VALUES (@Id, @Hour, @Minute, @ChatId) " +
            "ON DUPLICATE KEY UPDATE Hour = @Hour, Minute = @Minute, ChatId = @ChatId";

        public void AddAlarm(LunchAlarm alarm)
        {
            _database.Execute(AddAlarmQuery, AlarmData.Get(alarm));
        }

        public const string RemoveAlarmQuery = "DELETE FROM Alarms WHERE Id = @Id";
        
        public void RemoveAlarm(int id)
        {
            _database.Execute(RemoveAlarmQuery, new {Id = id});
        }

        private struct AlarmData
        {
            public int Id { get; set; }
            public int Hour { get; set; }
            public int Minute { get; set; }
            public long ChatId { get; set; }

            public static AlarmData Get(LunchAlarm alarm)
            {
                return new AlarmData
                {
                    Id = alarm.UserId,
                    Hour = alarm.Hour,
                    Minute = alarm.Minute,
                    ChatId = alarm.ChatId
                };
            }
        }
    }
}
