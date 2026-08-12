using System.ComponentModel;
using System.Runtime.CompilerServices;
using NewPCRPlayer.Services.Bbs;

namespace NewPCRPlayer.Models;

/// <summary>UI row for one BBS post (supports new-post highlight + formatted header).</summary>
public sealed class CommentItem : INotifyPropertyChanged
{
    private bool _isNew;
    private string _displayHeader = "";
    private string _displayHeaderNoNumber = "";
    private bool _showResBadge;

    public CommentItem(BbsPost post, AppSettings settings, bool isNew = false)
    {
        Post = post ?? throw new ArgumentNullException(nameof(post));
        _isNew = isNew;
        RefreshHeader(settings);
    }

    public BbsPost Post { get; }

    public int Number => Post.Number;
    public string BodyText => Post.BodyText;

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

    public bool IsNew
    {
        get => _isNew;
        set
        {
            if (_isNew == value) return;
            _isNew = value;
            OnPropertyChanged();
        }
    }

    public void RefreshHeader(AppSettings settings)
    {
        DisplayHeader = settings.FormatResHeader(Post, includeNumber: true);
        DisplayHeaderNoNumber = settings.FormatResHeader(Post, includeNumber: false);
        ShowResBadge = settings.ShowResNumber;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
