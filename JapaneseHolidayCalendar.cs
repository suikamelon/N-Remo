using System;

namespace N_Remo
{
    internal static class JapaneseHolidayCalendar
    {
        public static bool IsHoliday(DateTime source)
        {
            DateTime date = source.Date;
            if (IsBaseHoliday(date)) return true;

            // 国民の休日：祝日に挟まれた平日。
            if (date.Year >= 1986 &&
                IsBaseHoliday(date.AddDays(-1)) && IsBaseHoliday(date.AddDays(1)))
                return true;

            // 振替休日。2007年以降は連続する祝日の次の非祝日まで繰り越す。
            if (date.Year >= 1973)
            {
                DateTime cursor = date.AddDays(-1);
                while (IsBaseHoliday(cursor))
                {
                    if (cursor.DayOfWeek == DayOfWeek.Sunday) return true;
                    cursor = cursor.AddDays(-1);
                }
            }
            return false;
        }

        private static bool IsBaseHoliday(DateTime date)
        {
            int y = date.Year, m = date.Month, d = date.Day;
            if (m == 1 && d == 1) return true;
            if (m == 1 && ((y >= 2000 && IsNthMonday(date, 2)) ||
                           (y < 2000 && y >= 1949 && d == 15))) return true;
            if (m == 2 && d == 11 && y >= 1967) return true;
            if (m == 2 && d == 23 && y >= 2020) return true;
            if (m == 3 && d == VernalEquinoxDay(y) && y >= 1949) return true;
            if (m == 4 && d == 29 && y >= 1949) return true;
            if (m == 5 && d == 3) return true;
            if (m == 5 && d == 4 && y >= 2007) return true;
            if (m == 5 && d == 5) return true;
            if (m == 7 && y == 2020 && d == 23) return true;
            if (m == 7 && y == 2021 && d == 22) return true;
            if (m == 7 && y != 2020 && y != 2021 && y >= 2003 && IsNthMonday(date, 3)) return true;
            if (m == 7 && y >= 1996 && y < 2003 && d == 20) return true;
            if (m == 8 && y == 2020 && d == 10) return true;
            if (m == 8 && y == 2021 && d == 8) return true;
            if (m == 8 && y != 2020 && y != 2021 && y >= 2016 && d == 11) return true;
            if (m == 9 && y >= 2003 && IsNthMonday(date, 3)) return true;
            if (m == 9 && y >= 1966 && y < 2003 && d == 15) return true;
            if (m == 9 && d == AutumnEquinoxDay(y) && y >= 1948) return true;
            if (m == 10 && y == 2020 && d == 24) return true;
            if (m == 10 && y == 2021 && d == 23) return true;
            if (m == 10 && y != 2020 && y != 2021 && y >= 2000 && IsNthMonday(date, 2)) return true;
            if (m == 10 && y >= 1966 && y < 2000 && d == 10) return true;
            if (m == 11 && d == 3) return true;
            if (m == 11 && d == 23) return true;
            if (m == 12 && d == 23 && y >= 1989 && y <= 2018) return true;
            // 即位関連の一度限りの祝日。
            return (y == 2019 && ((m == 5 && d == 1) || (m == 10 && d == 22)));
        }

        private static bool IsNthMonday(DateTime date, int nth) =>
            date.DayOfWeek == DayOfWeek.Monday && (date.Day - 1) / 7 + 1 == nth;

        private static int VernalEquinoxDay(int year) =>
            (int)(20.8431 + 0.242194 * (year - 1980) - (year - 1980) / 4);

        private static int AutumnEquinoxDay(int year) =>
            (int)(23.2488 + 0.242194 * (year - 1980) - (year - 1980) / 4);
    }
}
