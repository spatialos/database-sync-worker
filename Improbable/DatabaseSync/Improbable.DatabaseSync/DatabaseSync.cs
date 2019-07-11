namespace Improbable.DatabaseSync
{
    public static class DatabaseSync
    {
        public static bool IsImmediateChild(string item, string parent)
        {
            return item.StartsWith(parent) && GetLevel(parent) == GetLevel(item) - 1;
        }

        public static bool IsChild(string item, string parent)
        {
            return item.StartsWith(parent);
        }

        public static int GetLevel(string path)
        {
            var count = 0;
            for (var i = 0; i < path.Length; i++)
            {
                if (path[i] == '.')
                {
                    count++;
                }
            }

            return count;
        }
    }
}
