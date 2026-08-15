using System.ComponentModel;
using System.Runtime.CompilerServices;
using DSPlayer.Services.Bbs;

namespace DSPlayer.Models;

/// <summary>UI row for one BBS post (supports new-post highlight + formatted header).</summary>
public sealed class CommentItem : INotifyPropertyChanged
{
    private bool _isNew;
    private string _displayHeader = "";
    private string _displayHeaderNoNumber = "";
    private bool _showResBadge;
    private bool _showNumber = true;
    private bool _showName = true;
    private bool _showDate = true;
    private int _idCount;

    public CommentItem(BbsPost post, AppSettings settings, bool isNew = false)
    {
        Post = post ?? throw new ArgumentNullException(nameof(post));
        if (isNew)
        {
            _isNew = true;
            HighlightUntilUtc = DateTime.UtcNow.AddSeconds(8);
        }
        RefreshHeader(settings);
    }

    public BbsPost Post { get; }

    public int Number => Post.Number;
    public string BodyText => Post.BodyText;
    public string NameText => Post.Name;
    public string DateId => Post.DateId;
    public string? PosterId => Post.PosterId;

    public string DisplayHeader
    {
        get => _displayHeader;
        private set
        {
            if (_displayHeader == value) return;
            _displayHeader = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Header without res# (Grok theme shows number in a separate pill).</summary>
    public string DisplayHeaderNoNumber
    {
        get => _displayHeaderNoNumber;
        private set
        {
            if (_displayHeaderNoNumber == value) return;
            _displayHeaderNoNumber = value;
            OnPropertyChanged();
        }
    }

    public bool ShowResBadge
    {
        get => _showResBadge;
        private set
        {
            if (_showResBadge == value) return;
            _showResBadge = value;
            OnPropertyChanged();
        }
    }

    public bool ShowNumber
    {
        get => _showNumber;
        private set
        {
            if (_showNumber == value) return;
            _showNumber = value;
            OnPropertyChanged();
        }
    }

    public bool ShowName
    {
        get => _showName;
        private set
        {
            if (_showName == value) return;
            _showName = value;
            OnPropertyChanged();
        }
    }

    public bool ShowDate
    {
        get => _showDate;
        private set
        {
            if (_showDate == value) return;
            _showDate = value;
            OnPropertyChanged();
        }
    }

    public int IdCount
    {
        get => _idCount;
        set
        {
            if (_idCount == value) return;
            _idCount = value;
            OnPropertyChanged();
        }
    }

    public DateTime HighlightUntilUtc { get; private set; }

    public bool IsNew
    {
        get => _isNew;
        set
        {
            if (_isNew == value) return;
            _isNew = value;
            HighlightUntilUtc = value
                ? DateTime.UtcNow.AddSeconds(8)
                : DateTime.MinValue;
            OnPropertyChanged();
        }
    }

    public void RefreshHeader(AppSettings settings)
    {
        DisplayHeader = settings.FormatResHeader(Post, includeNumber: true);
        DisplayHeaderNoNumber = settings.FormatResHeader(Post, includeNumber: false);
        ShowResBadge = settings.ShowResNumber;
        ShowNumber = settings.ShowResNumber;
        ShowName = settings.ShowResName;
        ShowDate = settings.ShowResDate;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
