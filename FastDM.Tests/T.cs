namespace FastDM.Tests
{
    // Tiny assertion helper: prints PASS/FAIL per check and gives the process exit code.
    static class T
    {
        static int pass, fail;

        public static void Section(string name) => Console.WriteLine("\n== " + name);

        public static void Check(string name, bool ok)
        {
            if (ok) pass++; else fail++;
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name);
        }

        public static int Done()
        {
            Console.WriteLine($"\n{pass} passed, {fail} failed");
            return fail == 0 ? 0 : 1;
        }
    }
}
