# Fork Details
This fork of DSharpPlus removes unsafe code execution from the library. 
The following changes are implemented:
- Removes unsafe buffers used during Zstd payload decompression.
- Removes references to third-party C libaries libzstd and sodium.
- Added overflow check for received messages. The maximum payload size (32 kb).

# DSharpPlus

An unofficial .NET wrapper for the Discord API, based off [DiscordSharp](https://github.com/suicvne/DiscordSharp), but rewritten to fit the API standards.

# Documentation

The documentation for the latest nightly version is available at [dsharpplus.github.io](https://dsharpplus.github.io/DSharpPlus).

## Resources

The following resources apply only for the latest stable version of the library.

### Tutorials

* [Making your first bot in C#](https://dsharpplus.github.io/DSharpPlus/articles/basics/bot_account.html).

### Example bots

* [Example by OoLunar](https://github.com/DSharpPlus/Example-Bots)
