using System.Collections.Generic;
using System.Windows;
using ExcelGitDiffViewer.Services;

namespace ExcelGitDiffViewer;

/// <summary>
/// Git コミット選択ダイアログ。履歴一覧から「変更前」「変更後」の2リビジョンを選ばせる。
/// </summary>
public partial class CommitPickerWindow : Window
{
    /// <summary>選択された変更前リビジョン。</summary>
    public GitCommitInfo? SelectedLeft { get; private set; }

    /// <summary>選択された変更後リビジョン。</summary>
    public GitCommitInfo? SelectedRight { get; private set; }

    public CommitPickerWindow(string filePath, IReadOnlyList<GitCommitInfo> history)
    {
        InitializeComponent();

        FileLabel.Text = $"対象: {filePath}";
        LeftList.ItemsSource = history;
        RightList.ItemsSource = history;

        // 既定: 右=ワークツリー（先頭）、左=直近コミット（2番目があれば）。
        if (history.Count > 0)
        {
            RightList.SelectedIndex = 0;
            LeftList.SelectedIndex = history.Count > 1 ? 1 : 0;
        }
    }

    private void OnCompareClick(object sender, RoutedEventArgs e)
    {
        if (LeftList.SelectedItem is not GitCommitInfo left || RightList.SelectedItem is not GitCommitInfo right)
        {
            MessageBox.Show(this, "変更前・変更後の両方を選択してください。", "選択が必要です",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedLeft = left;
        SelectedRight = right;
        DialogResult = true;
    }
}
