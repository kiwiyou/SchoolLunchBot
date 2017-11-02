using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
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
                body = body.Substring(body.IndexOf("searchSchul") + 13);
                var code = body.Substring(0, body.IndexOf("'"));
                body = body.Substring(body.IndexOf("학교주소") + 11);
                var address = body.Substring(0, body.IndexOf("<"));
                list.Add(new SchoolInfo(schoolName, code, address));
            }
            return list;
        }

        private static bool HasKorean(string str)
        {
            return str.Any(c => '가' <= c && '힣' >= c);
        }
    }

    public class SchoolInfo
    {
        public string Name { get; }
        public string Code { get; }
        public string Address { get; }

        public SchoolInfo(string name, string code, string address)
        {
            Name = name;
            Code = code;
            Address = address;
        }
    }
}
