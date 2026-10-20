using Agent.Core.Pipeline;
using Agent.Core.Resolver;

namespace Agent.Core.Tests.Pipeline;

/// <summary>Question kind names and rule 10 (which questions are typed).</summary>
public class QuestionKindsTests
{
    [Fact]
    public void ResolverKindsMapToTheirNames()
    {
        Assert.Equal("which_contact", QuestionKinds.Of(QuestionKind.WhichContact));
        Assert.Equal("did_you_mean", QuestionKinds.Of(QuestionKind.DidYouMean));
    }

    [Theory]
    [InlineData(QuestionKinds.WhichAmount, 0, QuestionAnswer.Typed)]
    [InlineData(QuestionKinds.WhichContact, 0, QuestionAnswer.Typed)]
    [InlineData(QuestionKinds.WhichAmount, 1, QuestionAnswer.Tap)]
    [InlineData(QuestionKinds.WhichContact, 2, QuestionAnswer.Tap)]
    [InlineData(QuestionKinds.WhichProduct, 0, QuestionAnswer.Tap)]
    [InlineData(QuestionKinds.BillOrStore, 2, QuestionAnswer.Tap)]
    [InlineData(QuestionKinds.PersonOrBill, 2, QuestionAnswer.Tap)]
    public void OnlyAMissingAmountOrRecipientIsTyped(string kind, int optionCount, QuestionAnswer expected)
        => Assert.Equal(expected, QuestionKinds.AnswerFor(kind, optionCount));
}
