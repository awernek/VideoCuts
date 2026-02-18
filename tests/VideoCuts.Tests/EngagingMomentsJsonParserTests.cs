using VideoCuts.Core.Models.CutSuggestion;
using VideoCuts.Infrastructure.EngagingMoments;
using Xunit;

namespace VideoCuts.Tests;

public class EngagingMomentsJsonParserTests
{
    [Fact]
    public void Parse_Null_ReturnsEmpty()
    {
        var result = EngagingMomentsJsonParser.Parse(null!);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmpty()
    {
        var result = EngagingMomentsJsonParser.Parse("");
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsEmpty()
    {
        var result = EngagingMomentsJsonParser.Parse("   ");
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_ValidJson_ReturnsVideoCuts()
    {
        var json = """{"cuts":[{"start":10.5,"end":45.2,"description":"Highlight"}]}""";
        var result = EngagingMomentsJsonParser.Parse(json);
        Assert.Single(result);
        Assert.Equal(10.5, result[0].StartSeconds);
        Assert.Equal(45.2, result[0].EndSeconds);
        Assert.Equal("Highlight", result[0].Description);
    }

    [Fact]
    public void Parse_ValidJson_MultipleCuts_ReturnsAllValid()
    {
        var json = """{"cuts":[{"start":0,"end":30,"description":"A"},{"start":60,"end":90,"description":"B"}]}""";
        var result = EngagingMomentsJsonParser.Parse(json);
        Assert.Equal(2, result.Count);
        Assert.Equal(0, result[0].StartSeconds);
        Assert.Equal(30, result[0].EndSeconds);
        Assert.Equal(60, result[1].StartSeconds);
        Assert.Equal(90, result[1].EndSeconds);
    }

    [Fact]
    public void Parse_ValidJson_WithSnakeCase_ReturnsVideoCuts()
    {
        var json = """{"cuts":[{"start":1,"end":2,"description":"x"}]}""";
        var result = EngagingMomentsJsonParser.Parse(json);
        Assert.Single(result);
        Assert.Equal(1, result[0].StartSeconds);
        Assert.Equal(2, result[0].EndSeconds);
    }

    [Fact]
    public void Parse_JsonWithMarkdownWrapper_StripsWrapper()
    {
        var json = "```json\n{\"cuts\":[{\"start\":5,\"end\":10,\"description\":\"d\"}]}\n```";
        var result = EngagingMomentsJsonParser.Parse(json);
        Assert.Single(result);
        Assert.Equal(5, result[0].StartSeconds);
        Assert.Equal(10, result[0].EndSeconds);
    }

    [Fact]
    public void Parse_InvalidRange_StartNegative_FiltersOut()
    {
        var json = """{"cuts":[{"start":-1,"end":10,"description":"bad"}]}""";
        var result = EngagingMomentsJsonParser.Parse(json);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_InvalidRange_EndNotGreaterThanStart_FiltersOut()
    {
        var json = """{"cuts":[{"start":10,"end":10,"description":"bad"},{"start":5,"end":3,"description":"bad2"}]}""";
        var result = EngagingMomentsJsonParser.Parse(json);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_ValidAndInvalidCuts_ReturnsOnlyValid()
    {
        var json = """{"cuts":[{"start":-1,"end":10,"description":"bad"},{"start":0,"end":20,"description":"ok"}]}""";
        var result = EngagingMomentsJsonParser.Parse(json);
        Assert.Single(result);
        Assert.Equal(0, result[0].StartSeconds);
        Assert.Equal(20, result[0].EndSeconds);
    }

    [Fact]
    public void Parse_EmptyCutsArray_ReturnsEmpty()
    {
        var json = """{"cuts":[]}""";
        var result = EngagingMomentsJsonParser.Parse(json);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_MissingCutsProperty_ReturnsEmpty()
    {
        var json = """{}""";
        var result = EngagingMomentsJsonParser.Parse(json);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsEmpty()
    {
        var json = """{invalid""";
        var result = EngagingMomentsJsonParser.Parse(json);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_NotJson_ReturnsEmpty()
    {
        var result = EngagingMomentsJsonParser.Parse("hello world");
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_DescriptionOptional_AcceptsNull()
    {
        var json = """{"cuts":[{"start":0,"end":1}]}""";
        var result = EngagingMomentsJsonParser.Parse(json);
        Assert.Single(result);
        Assert.Null(result[0].Description);
    }
}
