namespace MystiaStewardCompanion.Core;

public interface IRecommendationStateProvider
{
    string Description { get; }
    RecommendationState LoadState();
}
