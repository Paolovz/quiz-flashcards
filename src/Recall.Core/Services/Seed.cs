using Recall.Models;

namespace Recall.Services;

public static class Seed
{
    public static IEnumerable<Deck> Build()
    {
        yield return new Deck
        {
            Name = "Cell Structure",
            Subject = "Biology",
            Cards = new List<Card>
            {
                Card("What organelle produces ATP in eukaryotic cells?", "Mitochondria"),
                Card("Which organelle is the site of protein synthesis?", "Ribosome"),
                Card("What controls what enters and leaves a cell?", "Cell membrane | plasma membrane"),
                Card("What rigid layer surrounds plant cells?", "Cell wall"),
                Card("Which organelle packages and ships proteins?", "Golgi apparatus | Golgi body"),
                Card("What process converts light energy into chemical energy?", "Photosynthesis"),
                Card("What is the jelly-like fluid inside a cell called?", "Cytoplasm | cytosol"),
                Card("Which organelle contains digestive enzymes?", "Lysosome"),
                Card("Where is DNA found in a prokaryotic cell?", "Nucleoid"),
                Card("Which pigment absorbs light inside chloroplasts?", "Chlorophyll"),
            }
        };

        yield return new Deck
        {
            Name = "Present Tense",
            Subject = "Spanish",
            Cards = new List<Card>
            {
                Card("hablar — yo", "hablo"),
                Card("comer — tú", "comes"),
                Card("vivir — él / ella", "vive"),
                Card("tener — nosotros", "tenemos"),
                Card("ir — yo", "voy"),
                Card("ser — tú", "eres"),
                Card("hacer — ellos", "hacen"),
                Card("poder — yo", "puedo"),
            }
        };
        yield return new Deck
        {
            Name = "Card Types",
            Subject = "Demo",
            Cards = new List<Card>
            {
                Card("What organelle produces ATP?", "Mitochondria, the site of aerobic respiration"),
                new Card
                {
                    Kind = CardKind.MultipleChoice,
                    Front = "Which organelle is the site of aerobic respiration?",
                    Choices = new List<string> { "Mitochondrion", "Ribosome", "Golgi apparatus", "Lysosome" },
                    CorrectChoices = new List<int> { 0 },
                    Back = "Mitochondrion"
                },
                new Card
                {
                    Kind = CardKind.MultiSelect,
                    Front = "Which of these are noble gases?",
                    Choices = new List<string> { "Helium", "Oxygen", "Neon", "Iron" },
                    CorrectChoices = new List<int> { 0, 2 },
                    Back = "Helium | Neon"
                },
                new Card
                {
                    Kind = CardKind.FillBlanks,
                    Front = "The {{mitochondria}} is the site of aerobic respiration.",
                    Back = "mitochondria"
                },
                new Card
                {
                    Kind = CardKind.Order,
                    Front = "Sort these from smallest to largest.",
                    Items = new List<string> { "Atom", "Molecule", "Organelle", "Cell" },
                    Back = "Atom → Molecule → Organelle → Cell"
                },
                new Card
                {
                    Kind = CardKind.Match,
                    Front = "Match each organelle to its job.",
                    Pairs = new List<MatchPair>
                    {
                        new() { Left = "Ribosome", Right = "Builds proteins" },
                        new() { Left = "Lysosome", Right = "Digests waste" },
                        new() { Left = "Chloroplast", Right = "Captures light" }
                    },
                    Back = "Builds proteins | Digests waste | Captures light"
                },
                new Card
                {
                    Kind = CardKind.TrueFalse,
                    Front = "Plant cells contain chloroplasts.",
                    Back = "True"
                }
            }
        };
    }

    private static Card Card(string front, string back) => new() { Front = front, Back = back };
}
