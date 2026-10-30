using Sideclip.Classification;

namespace Sideclip.Tests;

public class SecretClassifierTests
{
    // LooksLikePassword

    [Fact]
    public void LooksLikePassword_EmptyString_ReturnsFalse()
        => Assert.False(SecretClassifier.LooksLikePassword(string.Empty));

    [Fact]
    public void LooksLikePassword_HasWhitespace_ReturnsFalse()
        => Assert.False(SecretClassifier.LooksLikePassword("correct horse battery staple"));

    [Fact]
    public void LooksLikePassword_IsUrl_ReturnsFalse()
        => Assert.False(SecretClassifier.LooksLikePassword("https://example.com/login"));

    [Fact]
    public void LooksLikePassword_IsWindowsPath_ReturnsFalse()
        => Assert.False(SecretClassifier.LooksLikePassword(@"C:\Users\a\file.txt"));

    [Fact]
    public void LooksLikePassword_IsEmail_ReturnsFalse()
        => Assert.False(SecretClassifier.LooksLikePassword("a@b.co"));

    [Fact]
    public void LooksLikePassword_IsGuid_ReturnsFalse()
        => Assert.False(SecretClassifier.LooksLikePassword("3f2504e0-4f89-11d3-9a0c-0305e82c3301"));

    [Fact]
    public void LooksLikePassword_StrongToken_ReturnsTrue()
        => Assert.True(SecretClassifier.LooksLikePassword("Tr0ub4dor&3xy"));

    [Fact]
    public void LooksLikePassword_SentenceWithPeriod_ReturnsFalse()
        => Assert.False(SecretClassifier.LooksLikePassword("Thequickbrownfoxjumpsoverthelazydog."));

    [Fact]
    public void LooksLikePassword_Whitespace_ReturnsFalse()
        => Assert.False(SecretClassifier.LooksLikePassword("   "));

    // LooksLikePasswordManagerPayload

    [Fact]
    public void PasswordManagerPayload_Passphrase_ReturnsTrue()
        => Assert.True(SecretClassifier.LooksLikePasswordManagerPayload("correct horse battery staple"));

    [Fact]
    public void PasswordManagerPayload_Url_ReturnsFalse()
        => Assert.False(SecretClassifier.LooksLikePasswordManagerPayload("https://example.com/login"));

    [Fact]
    public void PasswordManagerPayload_WindowsPath_ReturnsFalse()
        => Assert.False(SecretClassifier.LooksLikePasswordManagerPayload(@"C:\Users\a\file.txt"));

    [Fact]
    public void PasswordManagerPayload_Email_ReturnsFalse()
        => Assert.False(SecretClassifier.LooksLikePasswordManagerPayload("a@b.co"));

    [Fact]
    public void PasswordManagerPayload_Guid_ReturnsFalse()
        => Assert.False(SecretClassifier.LooksLikePasswordManagerPayload("3f2504e0-4f89-11d3-9a0c-0305e82c3301"));

    [Fact]
    public void PasswordManagerPayload_StrongToken_ReturnsTrue()
        => Assert.True(SecretClassifier.LooksLikePasswordManagerPayload("Tr0ub4dor&3xy"));

    [Fact]
    public void PasswordManagerPayload_LongProse_ReturnsFalse()
        => Assert.False(SecretClassifier.LooksLikePasswordManagerPayload(
            "The quick brown fox jumps over the lazy dog and then some more words here."));

    [Fact]
    public void PasswordManagerPayload_Whitespace_ReturnsFalse()
        => Assert.False(SecretClassifier.LooksLikePasswordManagerPayload("   "));

    // Combined Classify decisions

    [Theory]
    [InlineData("1Password.exe", "https://example.com", true)]
    [InlineData("chrome.exe", "correct horse battery staple", true)]
    [InlineData("chrome.exe", "https://example.com/foo", false)]
    [InlineData("notepad.exe", "correct horse battery staple", false)]
    [InlineData("1Password-BrowserSupport.exe", "x", true)]
    public void Classify_CombinedDecision(string owner, string text, bool expectedSecret)
    {
        var secret = ClipboardClassify(text, owner, null);
        Assert.Equal(expectedSecret, secret);
    }

    // Replicates the ClipboardWatcher classification logic for isolated testing.
    private static bool ClipboardClassify(string text, string? ownerExe, string? fgExe)
    {
        var fromOnePassword = Sideclip.Clipboard.ClipboardOwnerResolver.IsOnePasswordSource(ownerExe, fgExe);
        var fromBrowser = Sideclip.Clipboard.ClipboardOwnerResolver.IsBrowser(ownerExe)
                       || Sideclip.Clipboard.ClipboardOwnerResolver.IsBrowser(fgExe);

        if (fromOnePassword)
            return true;
        if (fromBrowser)
            return SecretClassifier.LooksLikePasswordManagerPayload(text)
                || SecretClassifier.LooksLikePassword(text);
        return SecretClassifier.LooksLikePassword(text);
    }
}
