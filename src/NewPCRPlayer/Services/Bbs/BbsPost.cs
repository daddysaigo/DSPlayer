namespace NewPCRPlayer.Services.Bbs;

public sealed class BbsPost
{
    public int Number { get; init; }
    public string Name { get; init; } = "";
    public string Mail { get; init; } = "";
    public string DateId { get; init; } = "";
    public string BodyHtml { get; init; } = "";
    public string BodyText { get; init; } = "";
    public string? Title { get; init; }

    public string DisplayHeader =>
        $"{Number} ：{Name}：{DateId}";
}
