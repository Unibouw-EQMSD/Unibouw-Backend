namespace UnibouwAPI.Helpers
{

    public static class DateTimeConvert
    {
        private static readonly TimeZoneInfo AmsterdamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

        /// Converts the given DateTime to Amsterdam time (CET/CEST).
        public static DateTime ToAmsterdamTime(DateTime dateTime)
        {
            if (dateTime.Kind == DateTimeKind.Utc)
            {
                return TimeZoneInfo.ConvertTimeFromUtc(dateTime, AmsterdamTimeZone);
            }

            if (dateTime.Kind == DateTimeKind.Local)
            {
                return TimeZoneInfo.ConvertTime(dateTime, TimeZoneInfo.Local, AmsterdamTimeZone);
            }

            // Unspecified → assume UTC (safe default for APIs)
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc), AmsterdamTimeZone);
        }
    }

}
