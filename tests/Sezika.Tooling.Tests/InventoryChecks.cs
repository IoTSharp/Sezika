using Sezika.Tooling;

internal static class InventoryChecks
{
    internal static void Run(string root, Action<string, Action> check)
    {
        static void Require(bool value) { if (!value) throw new InvalidDataException("Inventory fixture mismatch."); }
        static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new InvalidDataException("Malformed CSV accepted."); }
        check("CSV escaped quotes and commas", () => Require(InventoryOperations.ParseRow("\"a,b\",\"c\"\"d\"").SequenceEqual(new[] { "a,b", "c\"d" })));
        check("CSV unclosed and trailing fields rejected", () => { Reject(() => InventoryOperations.ParseRow("\"unclosed")); Reject(() => InventoryOperations.ParseRow("\"closed\"x")); });
        check("inventory fixture exact attribution", () =>
        {
            var path = Path.Combine(root, "inventory.csv");
            File.WriteAllText(path, "\"Path\",\"Tracking\",\"Extension\",\"Lines\",\"Group\",\"Risk\",\"Disposition\",\"ProposedTarget\",\"Status\"\n\"tools/rental-training/Askpass.cmd\",\"tracked\",\".cmd\",\"1\",\"租机训练与监护\",\"高\",\"old\",\"old\",\"PROPOSED\"\n");
            var result = InventoryOperations.Freeze(path, new Budget(10, null, CancellationToken.None));
            Require(result.Entries == 1 && result.Tracked == 1 && result.Assignments[0].WorkPackage == "T2" && !result.ImplementationsAccepted);
        });
        check("inventory duplicate paths rejected", () =>
        {
            var path = Path.Combine(root, "inventory-duplicate.csv");
            var lines = File.ReadAllLines(Path.Combine(root, "inventory.csv"));
            File.WriteAllLines(path, [lines[0], lines[1], lines[1]]);
            Reject(() => InventoryOperations.Freeze(path, new Budget(10, null, CancellationToken.None)));
        });
    }
}
