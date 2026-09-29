from ingestion.application.heading_detector import (
    Heading,
    detect_headings,
    find_heading_for_position,
)


def test_detect_markdown_atx_headings():
    text = """# Introduction
Some content here.
## Methods
More content.
### Subsection
Details."""
    headings = detect_headings(text)
    assert len(headings) == 3
    assert headings[0].text == "Introduction"
    assert headings[1].text == "Methods"
    assert headings[2].text == "Subsection"


def test_detect_markdown_setext_headings():
    text = """Introduction
============
Some content here.
Methods
-------
More content."""
    headings = detect_headings(text)
    assert len(headings) == 2
    assert headings[0].text == "Introduction"
    assert headings[1].text == "Methods"


def test_detect_numbered_sections():
    text = """1. Introduction
Some content.
1.1 Background
More content.
2. Methods
Details here.
2.1.3 Specific procedure
Even more details."""
    headings = detect_headings(text)
    assert len(headings) == 4
    assert headings[0].text == "1. Introduction"
    assert headings[1].text == "1.1 Background"
    assert headings[2].text == "2. Methods"
    assert headings[3].text == "2.1.3 Specific procedure"


def test_detect_allcaps_sections():
    text = """INTRODUCTION
This is the intro paragraph.
METHODS AND MATERIALS
This is the methods section.
RESULTS
These are the results."""
    headings = detect_headings(text)
    assert len(headings) == 3
    assert headings[0].text == "INTRODUCTION"
    assert headings[1].text == "METHODS AND MATERIALS"
    assert headings[2].text == "RESULTS"


def test_detect_title_case_headings():
    text = """Introduction to the Study
This is the intro paragraph that explains the study.
Methodology and Approach
This section describes the methodology used.
Results and Discussion
Here we present the results."""
    headings = detect_headings(text)
    assert len(headings) == 3
    assert headings[0].text == "Introduction to the Study"
    assert headings[1].text == "Methodology and Approach"
    assert headings[2].text == "Results and Discussion"


def test_does_not_detect_prose_as_heading():
    text = """This is a normal sentence that happens to be capitalized.
It should not be detected as a heading because it ends with a period.
This Is Another Sentence. That also should not be a heading.
Here is more prose that is just regular text."""
    headings = detect_headings(text)
    assert len(headings) == 0


def test_does_not_detect_very_long_lines_as_headings():
    text = (
        "This Is A Very Long Title Case Line That Goes On And On And Should Not Be "
        "Detected Because It Is Too Long To Be A Real Heading And Probably Just Prose\n"
        "Normal content here."
    )
    headings = detect_headings(text)
    assert len(headings) == 0


def test_mixed_heading_styles():
    text = """# Main Title
ABSTRACT
This is the abstract.
1. Introduction
Some intro text.
## Subsection
More details.
CONCLUSION
Final thoughts."""
    headings = detect_headings(text)
    assert len(headings) == 5
    assert headings[0].text == "Main Title"
    assert headings[1].text == "ABSTRACT"
    assert headings[2].text == "1. Introduction"
    assert headings[3].text == "Subsection"
    assert headings[4].text == "CONCLUSION"


def test_heading_positions_are_correct():
    text = """# First
content
## Second
more content"""
    headings = detect_headings(text)
    assert len(headings) == 2
    assert text[headings[0].start_char : headings[0].end_char] == "# First"
    assert text[headings[1].start_char : headings[1].end_char] == "## Second"


def test_find_heading_for_position_returns_nearest_preceding():
    headings = [
        Heading("Introduction", 0, 12),
        Heading("Methods", 50, 57),
        Heading("Results", 150, 157),
    ]
    assert find_heading_for_position(10, headings) == "Introduction"
    assert find_heading_for_position(60, headings) == "Methods"
    assert find_heading_for_position(100, headings) == "Methods"
    assert find_heading_for_position(160, headings) == "Results"
    assert find_heading_for_position(200, headings) == "Results"


def test_find_heading_for_position_before_first_heading_returns_none():
    headings = [
        Heading("Introduction", 50, 62),
        Heading("Methods", 100, 107),
    ]
    assert find_heading_for_position(0, headings) is None
    assert find_heading_for_position(30, headings) is None


def test_find_heading_for_position_empty_list_returns_none():
    assert find_heading_for_position(100, []) is None


def test_detect_headings_empty_text_returns_empty_list():
    assert detect_headings("") == []
    assert detect_headings("   \n\t  ") == []


def test_detect_headings_no_headings_returns_empty_list():
    text = """This is just regular prose.
It has no headings at all.
Just normal sentences.
Nothing that looks like a section header."""
    assert detect_headings(text) == []


def test_does_not_detect_single_word_as_heading():
    text = """Word
This is content.
Another
More content."""
    headings = detect_headings(text)
    assert len(headings) == 0


def test_does_not_detect_questions_as_headings():
    text = """What Is The Research Question?
This is the content explaining it.
How Was It Conducted?
More explanation here."""
    headings = detect_headings(text)
    assert len(headings) == 0


def test_handles_cjk_and_emoji_text():
    text = """# 序論 Introduction
日本語のコンテンツ。
## 方法論 Methods 🚀
More content here."""
    headings = detect_headings(text)
    assert len(headings) == 2
    assert "Introduction" in headings[0].text
    assert "Methods" in headings[1].text


def test_numbered_section_requires_capital_after_number():
    text = """1. this should not be detected
Normal content.
2. This Should Be Detected
More content."""
    headings = detect_headings(text)
    assert len(headings) == 1
    assert headings[0].text == "2. This Should Be Detected"


def test_allcaps_section_requires_minimum_length():
    text = """AB
Content here.
ABC
More content.
INTRODUCTION
Real section."""
    headings = detect_headings(text)
    assert len(headings) == 1
    assert headings[0].text == "INTRODUCTION"
