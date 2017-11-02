using System;
using System.Collections.Generic;
using SchoolFinder;

namespace SchoolLunch.Bot
{
    internal static class SchoolUtil
    {
        private static readonly Dictionary<Regions, string> RegionName = new Dictionary<Regions, string>
        {
            { Regions.Seoul, "서울" },
            { Regions.Incheon, "인천" },
            { Regions.Busan, "부산" },
            { Regions.Gwangju, "광주" },
            { Regions.Daejeon, "대전" },
            { Regions.Daegu, "대구" },
            { Regions.Sejong, "세종" },
            { Regions.Ulsan, "울산" },
            { Regions.Gyeonggi, "경기" },
            { Regions.Kangwon, "강원" },
            { Regions.Chungbuk, "충북" },
            { Regions.Chungnam, "충남" },
            { Regions.Gyeongbuk, "경북" },
            { Regions.Gyeongnam, "경남" },
            { Regions.Jeonbuk, "전북" },
            { Regions.Jeonnam, "전남" },
            { Regions.Jeju, "제주" }
        };
        private static readonly Dictionary<string, Regions> NameRegion = new Dictionary<string, Regions>
        {
            { "서울", Regions.Seoul },
            { "인천", Regions.Incheon },
            { "부산", Regions.Busan },
            { "광주", Regions.Gwangju },
            { "대전", Regions.Daejeon },
            { "대구", Regions.Daegu },
            { "세종", Regions.Sejong },
            { "울산", Regions.Ulsan },
            { "경기", Regions.Gyeonggi },
            { "강원", Regions.Kangwon },
            { "충북", Regions.Chungbuk },
            { "충남", Regions.Chungnam },
            { "경북", Regions.Gyeongbuk },
            { "경남", Regions.Gyeongnam },
            { "전북", Regions.Jeonbuk },
            { "전남", Regions.Jeonnam },
            { "제주", Regions.Jeju }
        };

        public static string GetRegionName(Regions region)
        {
            return RegionName[region];
        }

        public static Regions GetRegion(string name)
        {
            return NameRegion[name];
        }

        public static SchoolTypes GetSchoolType(string schoolName)
        {
            if (schoolName.EndsWith("초등학교") || schoolName.EndsWith("초"))
                return SchoolTypes.Elementary;
            if (schoolName.EndsWith("중학교") || schoolName.EndsWith("중"))
                return SchoolTypes.Middle;
            if (schoolName.EndsWith("고등학교") || schoolName.EndsWith("고"))
                return SchoolTypes.High;
            throw new ArgumentException("잘못된 학교명입니다.");
        }
    }
}
