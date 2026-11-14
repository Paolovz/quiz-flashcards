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

    /// <summary>A line as the layout reader reports it, with its glyph extents.</summary>
    private static PdfLine Placed(string text, double left, double right, double size = Body, double bold = 0, int page = 1)
        => new(page, text, size, bold, left, right);

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

    [Fact]
    public void A_full_width_line_is_still_rejoined_with_its_continuation()
    {
        var lines = new List<PdfLine>
        {
            Placed("Normal distribution (aka Gaussian distribution) is the most popular and useful continuous probability", 21.6, 576.2),
            Placed("distribution for a random variable, x. It describes data by two parameters.", 21.6, 276.6),
        };

        var prepared = CardFactory.PrepareDocument(lines);

        Assert.Contains(prepared, l => l.Text.Contains("continuous probability distribution for a random variable"));
    }

    [Fact]
    public void Short_list_rows_are_never_glued_into_one_sentence()
    {
        var lines = new List<PdfLine>
        {
            Placed("Exponential distribution (aka negative exponential distribution) is a continuous distribution often used", 21.6, 576.5),
            Placed("to measure the time that elapses between two occurrences of an event.", 21.6, 219.6),
            Placed("• Time required to complete a questionnaire", 39.6, 262.5),
            Placed("• Time between vehicle arrivals at a toll booth", 39.6, 256.3),
            Placed("• Distance between major defects in a highway", 39.6, 244.0),
        };

        var prepared = CardFactory.PrepareDocument(lines);

        Assert.DoesNotContain(prepared, l => l.Text.Contains("questionnaire Time between"));
        Assert.DoesNotContain(prepared, l => l.Text.Contains("toll booth Distance"));
        Assert.Contains(prepared, l => l.Text == "Time between vehicle arrivals at a toll booth");
    }

    [Fact]
    public void A_bullet_that_filled_the_column_still_starts_a_new_item()
    {
        var lines = new List<PdfLine>
        {
            Placed("• The standard deviation of the distribution determines the height and width of the", 39.6, 576.3),
            Placed("• The mean of the distribution determines the location of the centre of the graph,", 39.6, 576.2),
            Placed("thus changing the average.", 57.6, 197.2),
        };

        var prepared = CardFactory.PrepareDocument(lines);

        Assert.Contains(prepared, l => l.Text.StartsWith("The standard deviation") && l.Text.EndsWith("width of the"));
        Assert.Contains(prepared, l => l.Text.StartsWith("The mean of the distribution") && l.Text.EndsWith("thus changing the average."));
    }

    [Fact]
    public void A_placeholder_noun_is_never_used_as_a_term()
    {
        var cards = CardFactory.FromText("The name is a defined name for namespace.", 10);

        Assert.DoesNotContain(cards, c => c.Front.Equals("name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_definition_that_opens_on_a_participle_is_dropped()
    {
        var cards = CardFactory.FromText(
            "Instances of classes are created using the new keyword which allocates memory for a new instance " +
            "and returns a reference to it.", 10);

        Assert.DoesNotContain(cards, c => c.Front.StartsWith("Instances of classes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_blank_never_cuts_a_word_in_half()
    {
        var lines = new List<PdfLine>
        {
            Line("Class", Heading, 1),
            Line("A class is a data structure that defines data and actions in a single unit."),
            Line("Instances of classes are created using the new keyword which allocates memory for a new instance " +
                 "and returns a reference to it."),
        };

        var cards = CardFactory.FromLines(CardFactory.PrepareDocument(lines), 10);

        Assert.DoesNotContain(cards, c => c.Front.Contains("____es"));
        Assert.DoesNotContain(cards, c => c.Front.Contains("____s "));
    }

    [Fact]
    public void A_heading_over_a_bullet_list_becomes_one_card_answered_by_the_list()
    {
        var lines = new List<PdfLine>
        {
            Line("Properties of the Normal Curve", Heading, 1),
            Line("\u2022 The total area under the curve is one.", Body, 0, 1),
            Line("\u2022 The curve is symmetric about the mean.", Body, 0, 1),
            Line("\u2022 The mean, median and mode are equal.", Body, 0, 1),
        };

        var cards = CardFactory.FromLines(CardFactory.PrepareDocument(lines), 10);

        var card = Assert.Single(cards, c => c.Front == "Properties of the Normal Curve");
        Assert.Contains("1. The total area under the curve is one.", card.Back);
        Assert.Contains("3. The mean, median and mode are equal.", card.Back);
    }

    [Fact]
    public void Two_bullets_fall_back_to_a_definition_instead_of_a_list_card()
    {
        var lines = new List<PdfLine>
        {
            Line("Properties of the Normal Curve", Heading, 1),
            Line("\u2022 The total area under the curve is one.", Body, 0, 1),
            Line("\u2022 The curve is symmetric about the mean.", Body, 0, 1),
        };

        var cards = CardFactory.FromLines(CardFactory.PrepareDocument(lines), 10);

        var card = Assert.Single(cards, c => c.Front == "Properties of the Normal Curve");
        Assert.DoesNotContain("1. ", card.Back);
        Assert.Contains("symmetric about the mean", card.Back);
    }

    [Fact]
    public void A_definition_written_as_an_assignment_becomes_a_question()
    {
        var cards = CardFactory.FromText("median = the middle value of a data set.", 10);

        var card = Assert.Single(cards);
        Assert.Equal("What does median represent?", card.Front);
        Assert.Contains("middle value", card.Back);
    }

    [Fact]
    public void An_assignment_whose_right_side_is_a_number_is_not_a_card()
    {
        var cards = CardFactory.FromText("x = 5", 10);

        Assert.Empty(cards);
    }

    [Fact]
    public void A_formula_with_two_equals_signs_is_not_a_card()
    {
        var cards = CardFactory.FromText("z = (x - mean) / standard deviation = 1.5", 10);

        Assert.DoesNotContain(cards, c => c.Front.StartsWith("What does z", StringComparison.Ordinal));
    }

    [Fact]
    public void A_connector_before_the_symbol_is_not_part_of_the_symbol()
    {
        var cards = CardFactory.FromText("where Z = standard score", 10);

        var card = Assert.Single(cards);
        Assert.Equal("What does Z represent?", card.Front);
    }

    [Fact]
    public void Two_symbols_with_the_same_wording_are_two_cards()
    {
        var lines = new List<PdfLine>
        {
            Line("X = the value of any particular observation."),
            Line("\u03bc = the mean of the distribution."),
        };

        var cards = CardFactory.FromLines(CardFactory.PrepareDocument(lines), 10);

        Assert.Contains(cards, c => c.Front == "What does X represent?");
        Assert.Contains(cards, c => c.Front == "What does \u03bc represent?");
    }

    [Fact]
    public void A_bullet_list_survives_a_lead_in_line()
    {
        var lines = new List<PdfLine>
        {
            Line("Properties of the Normal Curve", Heading, 1),
            Line("\u2022 The normal distribution curve is symmetric about the mean.", Body, 0, 1),
            Line("For a distribution that is symmetrical and bell-shaped:", Body, 0, 1),
            Line("\u2022 68% of the values lie within 1 standard deviation.", Body, 0, 1),
            Line("\u2022 95% of the values lie within 2 standard deviations.", Body, 0, 1),
            Line("This shifts the mean of X to zero", Body, 0, 1),
        };

        var cards = CardFactory.FromLines(CardFactory.PrepareDocument(lines), 10);

        var card = Assert.Single(cards, c => c.Front == "Properties of the Normal Curve");
        Assert.Contains("1. The normal distribution curve", card.Back);
        Assert.Contains("3. 95% of the values", card.Back);
        Assert.DoesNotContain("shifts the mean", card.Back);
    }

    [Fact]
    public void A_blank_whose_answer_is_still_visible_in_the_sentence_is_dropped()
    {
        var lines = new List<PdfLine>
        {
            Line("Class", Heading, 1),
            Line("A class or structure can specify how accessible each of its members is to code " +
                 "outside of the class or structure."),
        };

        var cards = CardFactory.FromLines(CardFactory.PrepareDocument(lines), 10);

        Assert.DoesNotContain(cards, c => c.Kind == "cloze" &&
                                          c.Back.Equals("class", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_identifier_with_an_underscore_is_not_a_term()
    {
        var cards = CardFactory.FromText(
            "arguments_list refers to the actual value passed on the constructor or method.", 10);

        Assert.Empty(cards);
    }

    [Fact]
    public void A_list_answer_puts_one_item_per_line()
    {
        var lines = new List<PdfLine>
        {
            Line("Properties of the Normal Curve", Heading, 1),
            Line("• The normal distribution curve is symmetric about the mean."),
            Line("• Approximately 68% of the data values lie within 1 standard deviation."),
            Line("• Approximately 95% of the data values lie within 2 standard deviations."),
        };

        var cards = CardFactory.FromLines(CardFactory.PrepareDocument(lines), 10);

        var card = Assert.Single(cards, c => c.Kind == "list");
        Assert.Equal(3, card.Back.Split('\n').Length);
        Assert.DoesNotContain("mean. Approximately", card.Back);
    }

    [Fact]
    public void A_worked_example_lead_in_is_not_a_cloze()
    {
        const string prose =
            "For example, the constructor executes automatically and performs its statements to " +
            "initialise the instance variables of the object when the program starts running.";

        var cards = CardFactory.FromText(prose, 10);

        Assert.DoesNotContain(cards, c => c.Kind == "cloze");
    }

    [Fact]
    public void A_procedure_step_is_not_a_cloze()
    {
        const string prose =
            "Go to the row that represents the leading digit of the Z-value and record the second " +
            "decimal place of the value that appears in that row of the table.";

        var cards = CardFactory.FromText(prose, 10);

        Assert.DoesNotContain(cards, c => c.Kind == "cloze");
    }

    [Fact]
    public void A_cloze_never_starts_on_a_pronoun_without_its_antecedent()
    {
        const string prose =
            "A class is a blueprint. It defines dynamically created instances of the class, also " +
            "known as objects, and it allocates memory for every new instance.";

        var cards = CardFactory.FromText(prose, 10);

        Assert.DoesNotContain(cards, c => c.Kind == "cloze" &&
                                          c.Front.StartsWith("It ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_sentence_that_continues_the_one_above_it_is_not_a_cloze()
    {
        const string prose =
            "Then the members of the class or structure can only be accessed or updated by calling " +
            "the respective methods through the object that owns them.";

        var cards = CardFactory.FromText(prose, 10);

        Assert.DoesNotContain(cards, c => c.Kind == "cloze");
    }

    [Fact]
    public void A_cloze_never_quotes_a_variable_from_a_worked_example()
    {
        const string prose =
            "The constructor executes automatically and performs its statements to initialise the " +
            "instance variables of object p1 before the program continues to the next line.";

        var cards = CardFactory.FromText(prose, 10);

        Assert.DoesNotContain(cards, c => c.Kind == "cloze");
    }

    [Fact]
    public void Two_clauses_glued_by_a_dash_are_not_a_cloze()
    {
        const string prose =
            "Defining the modifier of class data members, like instance variables, as private – " +
            "This will make sure that data members of the class are not directly accessible.";

        var cards = CardFactory.FromText(prose, 10);

        Assert.DoesNotContain(cards, c => c.Kind == "cloze");
    }
}
