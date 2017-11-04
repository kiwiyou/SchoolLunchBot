using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace SchoolLunch.Bot
{
    public static class Schools
    {
        public static async Task<ICollection<SchoolInfo>> FindAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            var encodedName = HttpUtility.UrlEncode(name, Encoding.GetEncoding("euc-kr"));
            var param = new StringBuilder()
                .AppendFormat("&SEARCH_SCHUL_NM={0}", encodedName)
                .AppendFormat("&SEARCH_KEYWORD={0}", encodedName);
            var rawParam = Encoding.UTF8.GetBytes(param.ToString());
            var request = WebRequest.CreateHttp("http://www.schoolinfo.go.kr/ei/ss/Pneiss_f01_l0.do");
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = rawParam.LongLength;
                using (var reqStream = request.GetRequestStream())
                    reqStream.Write(rawParam, 0, rawParam.Length);
            string body;
            var response = await request.GetResponseAsync();
            var resStream = response.GetResponseStream();
            if (resStream == null)
                return null;
            try
            {
                using (var reader = new StreamReader(resStream, Encoding.Default))
                    body = await reader.ReadToEndAsync();
            }
            catch (IOException)
            {
                return null;
            }

            var list = new List<SchoolInfo>();
            while (true)
            {
                body = body.Substring(body.IndexOf("School_Name") + 76);
                var schoolName = body.Substring(0, body.IndexOf("<"));
                if (!HasKorean(schoolName))
                    break;
                body = body.Substring(body.IndexOf("mapD_Area") + 15);
                var office = ParseOffice(body.Substring(0, body.IndexOf("<")));
                if (office == EducationOffice.None) continue;
                body = body.Substring(body.IndexOf("mapD_Class") + 16);
                var type = ParseType(body.Substring(0, body.IndexOf("<")));
                if (type == SchoolType.None) continue;
                body = body.Substring(body.IndexOf("searchSchul") + 13);
                var code = body.Substring(0, body.IndexOf("'"));
                body = body.Substring(body.IndexOf("학교주소") + 11);
                var address = body.Substring(0, body.IndexOf("<"));
                list.Add(new SchoolInfo {
                    Name = schoolName,
                    Code = code,
                    Type = type,
                    Office = office,
                    Address = address
                });
            }
            return list;
        }

        private static bool HasKorean(string str)
        {
            return str.Any(c => '가' <= c && '힣' >= c);
        }

        public static SchoolType ParseType(string str)
        {
            switch (str)
            {
                case "초":
                    return SchoolType.Elementary;
                case "중":
                    return SchoolType.Middle;
                case "고":
                    return SchoolType.High;
                case "특":
                case "각":
                case "기타":
                    return SchoolType.Etc;
                default:
                    return SchoolType.None;
            }
        }

        public static EducationOffice ParseOffice(string region)
        {
            switch (region)
            {
                case "서울":
                    return EducationOffice.Seoul;
                case "인천":
                    return EducationOffice.Incheon;
                case "울산":
                    return EducationOffice.Ulsan;
                case "강원":
                    return EducationOffice.Gangwon;
                case "전북":
                    return EducationOffice.Jeonbuk;
                case "경남":
                    return EducationOffice.Gyeongnam;
                case "부산":
                    return EducationOffice.Busan;
                case "광주":
                    return EducationOffice.Gwangju;
                case "세종":
                    return EducationOffice.Sejong;
                case "충북":
                    return EducationOffice.Chungbuk;
                case "전남":
                    return EducationOffice.Jeonnam;
                case "제주":
                    return EducationOffice.Jeju;
                case "대구":
                    return EducationOffice.Daegu;
                case "대전":
                    return EducationOffice.Daejeon;
                case "경기":
                    return EducationOffice.Gyeonggi;
                case "충남":
                    return EducationOffice.Chungnam;
                case "경북":
                    return EducationOffice.Gyeongbuk;
                default:
                    return EducationOffice.None;
            }
        }
    }

    public class SchoolInfo
    {
        public string Name { get; set; }
        public string Code { get; set; }
        public SchoolType Type { get; set; }
        public EducationOffice Office { get; set; }
        public string Address { get; set; }
        
        public async Task<IDictionary<int, string>> GetLunch(int year, int month)
        {
            var addressBuilder = GetOfficeAddress(Office)
                .AppendFormat(
                    "/sts_sci_md00_001.do?schulCode={0}&schulCrseScCode={1}&schulKndScCode=0{2}&ay={3}&mm={4:D2}&",
                    Code, Convert.ToInt32(Type) + 1, Convert.ToInt32(Type) + 1, year, month);
            string body;
            using (var web = new WebClient {Encoding = Encoding.UTF8})
                body = await web.DownloadStringTaskAsync(addressBuilder.ToString());
            var tableIndex = body.IndexOf("<tbody>");
            if (tableIndex < 0)
                return null;
            body = body.Substring(tableIndex + 7, body.IndexOf("</tbody>") - tableIndex)
                .Replace("<tr>", "").Replace("</tr>", "").Replace(" class=\"last\"", "").Replace("<td>", "")
                .Replace("<div>", "").Replace("</div>", "").Replace("<br />", "\n").Replace("</td>", "@").Trim();

            var contents = body.Split('@');
            var result = new Dictionary<int, string>();
            foreach (var content in contents)
            {
                var raw = content.Trim().Split(new[] {'\n'}, 2);
                if (raw.Length != 2) continue;
                if (int.TryParse(raw[0], out int day))
                    result.Add(day, ReformatLunch(raw[1]));
            }
            return result;
        }

        private static readonly Regex RemoveRegex = new Regex("\\d{1,2}\\.|[sS]|\\*");
        private static string ReformatLunch(string lunch)
        {
            return RemoveRegex.Replace(lunch.Replace("&amp;", "&"), "");
        }

        private static StringBuilder GetOfficeAddress(EducationOffice office)
        {
            var builder = new StringBuilder("http://stu.");
            switch (office)
            {
                case EducationOffice.Seoul:
                    builder.Append("sen.go");
                    break;
                case EducationOffice.Incheon:
                    builder.Append("ice.go");
                    break;
                case EducationOffice.Ulsan:
                    builder.Append("use.go");
                    break;
                case EducationOffice.Gangwon:
                    builder.Append("kwe.go");
                    break;
                case EducationOffice.Jeonbuk:
                    builder.Append("jbe.go");
                    break;
                case EducationOffice.Gyeongnam:
                    builder.Append("gne.go");
                    break;
                case EducationOffice.Busan:
                    builder.Append("pen.go");
                    break;
                case EducationOffice.Gwangju:
                    builder.Append("gen.go");
                    break;
                case EducationOffice.Sejong:
                    builder.Append("sje.go");
                    break;
                case EducationOffice.Chungbuk:
                    builder.Append("cbe.go");
                    break;
                case EducationOffice.Jeonnam:
                    builder.Append("jne.go");
                    break;
                case EducationOffice.Jeju:
                    builder.Append("jje.go");
                    break;
                case EducationOffice.Daegu:
                    builder.Append("dge.go");
                    break;
                case EducationOffice.Daejeon:
                    builder.Append("dje.go");
                    break;
                case EducationOffice.Gyeonggi:
                    builder.Append("goe.go");
                    break;
                case EducationOffice.Chungnam:
                    builder.Append("cne.go");
                    break;
                case EducationOffice.Gyeongbuk:
                    builder.Append("gbe");
                    break;
            }
            return builder.Append(".kr");
        }
    }

    public enum SchoolType
    {
        None,
        Elementary,
        Middle,
        High,
        Etc
    }

    public enum EducationOffice
    {
        None,
        Seoul, 
        Incheon,
        Ulsan,
        Gangwon,
        Jeonbuk,
        Gyeongnam,
        Busan,
        Gwangju,
        Sejong,
        Chungbuk,
        Jeonnam,
        Jeju,
        Daegu,
        Daejeon,
        Gyeonggi,
        Chungnam,
        Gyeongbuk
    }
}
