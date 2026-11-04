namespace SaveSync
{
    public static class Texts
    {
        public static string L(string english, string russian)
        {
            Localization.Translation translation = Localization.CurrentTranslation;
            if (translation != null && Mentions(translation.Root))
            {
                return russian;
            }
            return english;
        }

        private static bool Mentions(string path)
        {
            return path != null && path.IndexOf("Russian") >= 0;
        }
    }
}
