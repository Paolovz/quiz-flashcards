using Recall.Services;

namespace Recall.Tests;

/// <summary>
/// Regression tests taken from the two handout PDFs that shipped with the project. Every
/// case here produced a card nobody could answer before the layout-aware rewrite.
/// </summary>
public class PdfImportQualityTests
{
    private const double Body = 11.0;
    private const double Heading = 15.0;

    private static PdfLine Line(string text, double size = Body, double bold = 0, int page = 1)
        => new(page, text, size, bold, 0);

    [Fact]
    public void A_heading_never_glues_onto_the_sentence_below_it()
    {
        var lines = new List<PdfLine>
        {
            Line("Class", Heading, 1),
            Line("A class is a data structure or a blueprint that defines data and actions in a single unit."),
        };

        var cards = CardFactory.FromLines(CardFactory.PrepareDocument(lines), 10);

        var card = Assert.Single(cards);
        Assert.Equal("class", card.Front, ignoreCase: true);
        Assert.DoesNotContain("Class A class", card.Front);
    }

    [Fact]
    public void A_bold_run_becomes_the_term_even_without_a_size_jump()
    {
        var lines = new List<PdfLine>
        {
            Line("Encapsulation", Body, 1),
            Line("The bundling of data and the methods that operate on that data inside one unit."),
        };

        var cards = CardFactory.FromLines(CardFactory.PrepareDocument(lines), 10);

        var card = Assert.Single(cards);
        Assert.Equal("Encapsulation", card.Front);
        Assert.Contains("bundling of data", card.Back);
    }

    [Fact]
    public void A_clause_is_never_used_as_a_term()
    {
        const string prose =
            "Methods and variables that are not intended to be used outside of the class can be hidden. " +
            "This limits the potential for coding errors in a large program.";

        var cards = CardFactory.FromText(prose, 10);

        Assert.DoesNotContain(cards, c => c.Front.StartsWith("Methods and variables that"));
        Assert.DoesNotContain(cards, c => c.Front.Contains(" that"));
    }

    [Fact]
    public void Question_and_solution_lines_never_become_cards()
    {
        const string prose =
            "What is the probability that the elevator takes less than 15 seconds to arrive? " +
            "Solution: In this case the interval of the probability distribution is zero to forty.";

        var cards = CardFactory.FromText(prose, 10);

        Assert.DoesNotContain(cards, c => c.Front.Equals("What", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(cards, c => c.Front.Equals("Solution", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(cards, c => c.Front.Contains('?'));
    }

    [Fact]
    public void A_cloze_blank_is_never_an_adverb()
    {
        const string prose =
            "Then, the constructor executes automatically and performs its statements to initialise " +
            "the instance variables of the object. After using a property name, the respective get " +
            "and set methods are invoked.";

        var cards = CardFactory.FromText(prose, 10);

        Assert.DoesNotContain(cards, c => c.Back.Equals("automatically", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(cards, c => c.Back.Equals("respective", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_cloze_blank_comes_from_a_term_the_document_established()
    {
        var lines = new List<PdfLine>
        {
            Line("Namespace", Heading, 1),
            Line("A namespace provides a logical grouping to organise related classes and interfaces."),
            Line("A defined namespace can be imported into any other program to reuse its content."),
        };

        var cards = CardFactory.FromLines(CardFactory.PrepareDocument(lines), 10);
        var cloze = cards.Where(c => c.Kind == "cloze").ToList();

        Assert.NotEmpty(cloze);
        Assert.All(cloze, c => Assert.Equal("namespace", c.Back, ignoreCase: true));
    }

    [Fact]
    public void A_reference_list_is_dropped_entirely()
    {
        var lines = new List<PdfLine>
        {
            Line("Encapsulation is the bundling of data and the methods that operate on that data."),
            Line("REFERENCES", Heading, 1),
            Line("Deitel, P. and Deitel, H. C# How to Program. Pearson Education, 2017."),
        };

        var cards = CardFactory.FromLines(CardFactory.PrepareDocument(lines), 10);

        Assert.DoesNotContain(cards, c => c.Front.Contains("REFERENCES", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(cards, c => c.Back.Contains("Deitel"));
    }

    [Fact]
    public void Code_lines_are_dropped()
    {
        var lines = new List<PdfLine>
        {
            Line("Constructors", Heading, 1),
            Line("A constructor is a special method whose name matches the name of its type."),
            Line("Person p1 = new Person();"),
            Line("public Person() { //statement(s) to initialize object }"),
        };

        var cards = CardFactory.FromLines(CardFactory.PrepareDocument(lines), 10);

        Assert.All(cards, c => Assert.DoesNotContain("Person p1 =", c.Front));
        Assert.All(cards, c => Assert.DoesNotContain("//statement", c.Back));
    }

    [Fact]
    public void Wrapped_lines_are_rejoined_before_sentences_are_split()
    {
        var lines = new List<PdfLine>
        {
            Line("Mitochondria are the organelles that produce most of the cell's ATP through"),
            Line("oxidative phosphorylation inside the matrix."),
        };

        var cards = CardFactory.FromLines(CardFactory.PrepareDocument(lines), 10);

        var card = Assert.Single(cards);
        Assert.Equal("Mitochondria", card.Front);
        Assert.Contains("oxidative phosphorylation", card.Back);
    }

    [Fact]
    public void A_running_header_is_removed_across_pages()
    {
        var lines = new List<PdfLine>
        {
            Line("Computer Programming 1", Heading, 1, page: 1),
            Line("A class is a data structure that defines data and actions in a single unit.", page: 1),
            Line("Computer Programming 1", Heading, 1, page: 2),
            Line("Encapsulation is the bundling of data with the methods that operate on it.", page: 2),
            Line("Computer Programming 1", Heading, 1, page: 3),
        };

        var prepared = CardFactory.PrepareDocument(lines);

        Assert.DoesNotContain(prepared, l => l.Text.Contains("Computer Programming 1"));
        Assert.Contains(prepared, l => l.Text.StartsWith("A class is"));
    }
}
