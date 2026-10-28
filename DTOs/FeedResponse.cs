using UrubuApi.Models;

namespace UrubuApi.DTOs;

public record PaginatedItemsResponse(
    IReadOnlyList<CuratedItem> items,
    int total,
    int page,
    int limit,
    int totalPages,
    bool hasMore
);

public record FeedResponse(
    IReadOnlyList<CuratedItem> Items,
    int TotalItems,
    int Page,
    int TotalPages,
    IReadOnlyList<string> AvailableCategories,
    DateTime GeneratedAt
);

public record FeedbackRequest(string? Feedback);

public record HuntRequest(string? domain, List<string>? targetStores);

public record HuntStatusDto(
    bool isRunning,
    string? domain,
    string? lastRun,
    int itemsFound,
    int itemsCurated,
    string? error
);

public record ClearFeedResponse(
    bool success,
    int cleared,
    int preserved,
    string message
);
