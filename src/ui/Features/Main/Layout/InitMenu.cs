using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.ValueConverters;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nikse.SubtitleEdit.Features.Main.Layout;

public static class InitMenu
{
    // One notch below the Fluent theme default (~14).
    private const double MenuFontSize = 13.0;

    // One stateless instance, shared by every window: it reads the menu and the view model
    // back off the event rather than capturing them, so Make() can detach-then-attach it
    // (see there) without a per-window field and without ever stacking subscriptions.
    private static readonly EventHandler<Avalonia.Interactivity.RoutedEventArgs> OpenedHandler = (s, e) =>
    {
        if (s is Menu openedMenu && openedMenu.DataContext is MainViewModel menuVm)
        {
            DisplayShortcuts(openedMenu, menuVm);
        }
    };

    /// <summary>
    /// Guards a borrowed Shortcuts-dialog label. Those keys ship as empty strings in some
    /// translations (Dutch, for one), and an empty value in the JSON overrides the English
    /// default - so a menu entry using one would render as a blank row.
    /// </summary>
    private static string OrFallback(string? label, string fallback)
    {
        return string.IsNullOrWhiteSpace(label) ? fallback : label;
    }

    public static void Make(MainViewModel vm)
    {
        var l = Se.Language.Main.Menu;

        vm.MenuReopen = new MenuItem
        {
            Header = l.Reopen,
            Command = vm.CommandFileReopenCommand,
        };

        UpdateRecentFiles(vm);

        var menu = vm.Menu;
        menu.DataContext = vm;
        menu.Items.Clear();

        // vm.Menu is a single long-lived control and Make() re-runs on every language
        // switch, so the handler and the style below have to be detached first - otherwise
        // they stack up one copy per run and the gestures get stamped N times per open.
        menu.Opened -= OpenedHandler;
        menu.Opened += OpenedHandler;

        // Drop the menu's font one notch below the theme default and tighten
        // each item's vertical padding — a denser menu reads better when there
        // are this many entries. The style targets nested MenuItems so submenu
        // items inherit the same look.
        menu.FontSize = MenuFontSize;
        menu.Styles.Clear();
        menu.Styles.Add(new Style(x => x.OfType<MenuItem>())
        {
            Setters =
            {
                new Setter(MenuItem.FontSizeProperty, MenuFontSize),
                new Setter(MenuItem.PaddingProperty, new Thickness(10, 1)),
                new Setter(MenuItem.MinHeightProperty, 23.0),
            },
        });

        menu.Items.Add(new MenuItem
        {
            Header = l.File,
            Items =
            {
                new MenuItem
                {
                    Header = l.New,
                    Command = vm.CommandFileNewCommand,
                },
                new MenuItem
                {
                    Header = l.NewKeepVideo,
                    Command = vm.CommandFileNewKeepVideoCommand,
                    [!MenuItem.IsVisibleProperty] = new Binding(nameof(vm.IsVideoLoaded)),
                },
                new MenuItem
                {
                    Header = l.NewWindow,
                    Command = vm.CommandFileNewWindowCommand,
                },
                new Separator(),
                new MenuItem
                {
                    Header = l.Open,
                    Command = vm.CommandFileOpenCommand,
                },
                new MenuItem
                {
                    Header = l.OpenKeepVideo,
                    Command = vm.CommandFileOpenKeepVideoCommand,
                    [!MenuItem.IsVisibleProperty] = new Binding(nameof(vm.IsVideoLoaded)),
                },
                new MenuItem
                {
                    Header = l.OpenOriginal,
                    Command = vm.FileOpenOriginalCommand,
                    [!MenuItem.IsVisibleProperty] = new Binding(nameof(vm.ShowColumnOriginalText)) { Converter = new InverseBooleanConverter() }
                },
                new MenuItem
                {
                    Header = l.CloseOriginal,
                    Command = vm.FileCloseOriginalCommand,
                    [!MenuItem.IsVisibleProperty] = new Binding(nameof(vm.ShowColumnOriginalText))
                },
                new MenuItem
                {
                    Header = l.CloseTranslation,
                    Command = vm.FileCloseTranslationCommand,
                    [!MenuItem.IsVisibleProperty] = new Binding(nameof(vm.ShowColumnOriginalText))
                },
                vm.MenuReopen,
                new MenuItem
                {
                    Header = l.RestoreAutoBackup,
                    Command = vm.ShowRestoreAutoBackupCommand,
                },
                new Separator(),
                new MenuItem
                {
                    Header = l.Save,
                    Command = vm.CommandFileSaveCommand,
                },
                new MenuItem
                {
                    Header = l.SaveAs,
                    Command = vm.CommandFileSaveAsCommand,
                },
                new Separator(),
                new MenuItem
                {
                    [!MenuItem.HeaderProperty] = new Binding(nameof(vm.FilePropertiesText)),
                    [!MenuItem.IsVisibleProperty] = new Binding(nameof(vm.IsFilePropertiesVisible)),
                    Command = vm.FilePropertiesShowCommand,
                    DataContext = vm,
                },
                new MenuItem
                {
                    Header = l.OpenContainingFolder,
                    Command = vm.OpenContainingFolderCommand,
                },
                new MenuItem
                {
                    Header = l.Compare,
                    Command = vm.ShowCompareCommand,
                },
                new MenuItem
                {
                    Header = l.Statistics,
                    Command = vm.ShowStatisticsCommand,
                },
                new Separator(),
                new MenuItem
                {
                    Header = l.Import,
                    Items =
                    {
                        new MenuItem
                        {
                            Header = Se.Language.File.Import.SubtitleWithManuallyChosenEncodingDotDotDot,
                            Command = vm.ShowImportSubtitleWithManuallyChosenEncodingCommand,
                        },
                        new Separator(),
                        new MenuItem
                        {
                            Header = Se.Language.File.Import.ImageBasedSubtitleForOcrDotDotDot,
                            Command = vm.ImportImageSubtitleForOcrCommand,
                        },
                        new MenuItem
                        {
                            Header = Se.Language.File.Import.ImageBasedSubtitleForEditDotDotDot,
                            Command = vm.ImportImageSubtitleForEditCommand,
                        },
                        new MenuItem
                        {
                            Header = Se.Language.File.Import.ImagesForOcrDotDotDot,
                            Command = vm.ImportImagesCommand,
                        },
                        new MenuItem
                        {
                            Header = Se.Language.File.Import.PlainTextDotDotDot,
                            Command = vm.ImportPlainTextCommand,
                        },
                        new MenuItem
                        {
                            Header = Se.Language.File.Import.CsvXlsxCustomColumnsDotDotDot,
                            Command = vm.ImportCsvXlsxCustomColumnsCommand,
                        },
                        new Separator(),
                        new MenuItem
                        {
                            Header = Se.Language.File.Import.TimeCodesDotDotDot,
                            Command = vm.ImportTimeCodesCommand,
                        },
                        new MenuItem
                        {
                            Header = Se.Language.File.Import.FormattingDotDotDot,
                            Command = vm.ImportStylingCommand,
                        },
                    }
                },
                new MenuItem
                {
                    Header = l.Export,
                    Items =
                    {
                        // Text formats first - they are the exports actually performed here.
                        // The image/broadcast formats below are all kept, just demoted.
                        new MenuItem
                        {
                            Header = Se.Language.File.Export.CustomTextFormatsDotDotDot,
                            Command = vm.ShowExportCustomTextFormatCommand,
                        },
                        new MenuItem
                        {
                            Header = Se.Language.File.Export.PlainTextDotDotDot,
                            Command = vm.ShowExportPlainTextCommand,
                        },
                        new Separator(),
                        new MenuItem
                        {
                            Header = Se.Language.General.BluRaySup,
                            Command = vm.ExportBluRaySupCommand,
                        },
                        new MenuItem
                        {
                            Header = Se.Language.General.BdnXml,
                            Command = vm.ExportBdnXmlCommand,
                        },
                        new MenuItem
                        {
                            Header = "IMSC 1.1 image profile",
                            Command = vm.ExportImscImageCommand,
                        },
                        new MenuItem
                        {
                            Header = new CapMakerPlus().Name,
                            Command = vm.ExportCapMakerPlusCommand,
                        },
                        new MenuItem
                        {
                            Header = CheetahCaption.NameOfFormat,
                            Command = vm.ExportCheetahCaptionCommand,
                        },
                        new MenuItem
                        {
                            Header = CheetahCaptionOld.NameOfFormat,
                            Command = vm.ExportCheetahCaptionOldCommand,
                        },
                        new MenuItem
                        {
                            Header = Cavena890.NameOfFormat,
                            Command = vm.ExportCavena890Command,
                        },
                        new MenuItem
                        {
                            Header = Se.Language.File.Export.TitleExportDCinemaInteropPng,
                            Command = vm.ExportDCinemaInteropPngCommand,
                        },
                        new MenuItem
                        {
                            Header = Se.Language.File.Export.TitleExportDCinemaSmpte2014Png,
                            Command = vm.ExportDCinemaSmpte2014PngCommand,
                        },
                        new MenuItem
                        {
                            Header = Ebu.NameOfFormat,
                            Command = vm.ExportEbuStlCommand,
                        },
                        new MenuItem
                        {
                            Header = "DOST/png",
                            Command = vm.ExportDostPngCommand,
                        },
                        new MenuItem
                        {
                            Header = Se.Language.File.Export.TitleExportDvdSup,
                            Command = vm.ExportDvdSupCommand,
                        },
                        new MenuItem
                        {
                            Header = "Final Cut Pro + image",
                            Command = vm.ExportFcpPngCommand,
                        },
                        new MenuItem
                        {
                            Header = Se.Language.General.ImagesWithTimeCode,
                            Command = vm.ExportImagesWithTimeCodeCommand,
                        },
                        new MenuItem
                        {
                            Header = Pac.NameOfFormat,
                            Command = vm.ExportPacCommand,
                        },
                        new MenuItem
                        {
                            Header = new PacUnicode().Name,
                            Command = vm.ExportPacUnicodeCommand,
                        },
                        new MenuItem
                        {
                            Header = Se.Language.File.Export.TitleExportVobSub,
                            Command = vm.ExportVobSubCommand,
                        },
                        new MenuItem
                        {
                            Header = "WebVTT png",
                            Command = vm.ExportWebVttThumbnailsCommand,
                        },
                    }
                },
                new Separator(),
                new MenuItem
                {
                    Header = l.Exit,
                    Command = vm.CommandExitCommand,
                }
            }
        });

        menu.Items.Add(new MenuItem
        {
            Header = l.Edit,
            Items =
            {
                new MenuItem
                {
                    Header = l.Undo,
                    Command = vm.UndoCommand,
                },
                new MenuItem
                {
                    Header = l.Redo,
                    Command = vm.RedoCommand,
                },
                new MenuItem
                {
                    Header = l.ShowHistory,
                    Command = vm.ShowHistoryCommand,
                },
                new Separator(),
                new MenuItem
                {
                    Header = l.Find,
                    Command = vm.ShowFindCommand,
                },
                new MenuItem
                {
                    Header = l.FindNext,
                    Command = vm.FindNextCommand,
                },
                new MenuItem
                {
                    Header = l.Replace,
                    Command = vm.ShowReplaceCommand,
                },
                new MenuItem
                {
                    Header = l.MultipleReplace,
                    Command = vm.ShowMultipleReplaceCommand,
                },
                new MenuItem
                {
                    Header = l.GoToLineNumber,
                    Command = vm.ShowGoToLineCommand,
                },
                new Separator(),
                new MenuItem
                {
                    Header = l.RightToLeftMode,
                    Command = vm.RightToLeftToggleCommand,
                    [!Visual.IsVisibleProperty] = new Binding(nameof(vm.IsRightToLeftEnabled)),
                    Icon = new Optris.Icons.Avalonia.Icon
                    {
                        Value = IconNames.CheckBold,
                        VerticalAlignment = VerticalAlignment.Center,
                    }
                },
                new MenuItem
                {
                    Header = l.RightToLeftMode,
                    Command = vm.RightToLeftToggleCommand,
                    [!Visual.IsVisibleProperty] = new Binding(nameof(vm.IsRightToLeftEnabled)) { Converter = new InverseBooleanConverter() },
                },
                new MenuItem
                {
                    Header = l.FixRightToLeftViaUnicodeControlCharacters,
                    Command = vm.FixRightToLeftViaUnicodeControlCharactersCommand,
                },
                new MenuItem
                {
                    Header = l.RemoveUnicodeControlCharacters,
                    Command = vm.RemoveUnicodeControlCharactersCommand,
                },
                new MenuItem
                {
                    Header = l.ReverseRightToLeftStartEnd,
                    Command = vm.ReverseRightToLeftStartEndCommand,
                },
                new Separator(),
                new MenuItem
                {
                    Header = l.ModifySelectionDotDotDot,
                    Command = vm.ShowModifySelectionCommand,
                },
                new MenuItem
                {
                    Header = Se.Language.General.InvertSelection,
                    Command = vm.InverseSelectionCommand,
                },
                new MenuItem
                {
                    Header = Se.Language.General.SelectAll,
                    Command = vm.SelectAllLinesCommand,
                },
            }
        });

        var menuItemTools = new MenuItem
        {
            Header = l.Tools,
        };
        menu.Items.Add(menuItemTools);
        // Grouped by what the command does, not by the accident of its English name.
        // Upstream sorted this list alphabetically at build time, which split obvious
        // siblings ("Adjust durations" from "Apply duration limits") and re-shuffled the
        // whole menu in every other UI language. The six timing commands that used to live
        // here now sit in Synchronization, and "Make new empty translation" in Translate.
        foreach (var item in new List<Control>
        {
            // Find and fix
            new MenuItem
            {
                Header = l.FixCommonErrors,
                Command = vm.ShowToolsFixCommonErrorsCommand,
            },
            new MenuItem
            {
                Header = l.CheckAndFixNetflixErrors,
                Command = vm.ShowToolsFixNetflixErrorsCommand,
            },
            new MenuItem
            {
                Header = l.AiReview,
                Command = vm.ShowToolsAiReviewCommand,
            },
            new MenuItem
            {
                Header = l.ChangeCasing,
                Command = vm.ShowToolsChangeCasingCommand,
            },
            new MenuItem
            {
                Header = l.ChangeFormatting,
                Command = vm.ShowToolsChangeFormattingCommand,
            },
            new MenuItem
            {
                Header = l.RemoveTextForHearingImpaired,
                Command = vm.ShowToolsRemoveTextForHearingImpairedCommand,
            },
            new MenuItem
            {
                Header = l.ConvertActors,
                Command = vm.ShowToolsConvertActorsCommand,
            },
            new Separator(),

            // Merge lines
            new MenuItem
            {
                Header = l.MergeLinesWithSameText,
                Command = vm.ShowToolsMergeLinesWithSameTextCommand,
            },
            new MenuItem
            {
                Header = l.MergeLinesWithSameTimeCodes,
                Command = vm.ShowToolsMergeLinesWithSameTimeCodesCommand,
            },
            new MenuItem
            {
                Header = l.MergeShortLines,
                Command = vm.ShowToolsMergeShortLinesCommand,
            },
            new MenuItem
            {
                Header = l.MergeContinuationLines,
                Command = vm.ShowToolsMergeContinuationLinesCommand,
            },
            new Separator(),

            // Split lines and re-order
            new MenuItem
            {
                Header = l.SplitBreakLongLines,
                Command = vm.ShowToolsSplitBreakLongLinesCommand,
            },
            new MenuItem
            {
                Header = l.Renumber,
                Command = vm.ShowToolsRenumberCommand,
            },
            new MenuItem
            {
                Header = l.SortSubtitles,
                Command = vm.ShowSortByCommand,
            },
            new Separator(),

            // Whole-file operations
            new MenuItem
            {
                Header = l.MergeTwoSubtitles,
                Command = vm.ShowToolsMergeTwoSubtitlesCommand,
            },
            new MenuItem
            {
                Header = l.JoinSubtitles,
                Command = vm.ShowToolsJoinCommand,
            },
            new MenuItem
            {
                Header = l.SplitSubtitle,
                Command = vm.ShowToolsSplitCommand,
            },
            new MenuItem
            {
                Header = l.BatchConvert,
                Command = vm.ShowToolsBatchConvertCommand,
            },
        })
        {
            menuItemTools.Items.Add(item);
        }

        vm.MenuPlugins.Header = Se.Language.Plugins.Title;
        vm.MenuPlugins.IsVisible = Se.Settings.Appearance.ShowPluginsMenu;
        UpdatePluginsMenu(vm);
        menu.Items.Add(vm.MenuPlugins);

        menu.Items.Add(new MenuItem
        {
            Header = l.SpellCheckTitle,
            Items =
            {
                new MenuItem
                {
                    Header = l.SpellCheck,
                    Command = vm.ShowSpellCheckCommand,
                },
                new MenuItem
                {
                    Header = l.FindDoubleWords,
                    Command = vm.ShowFindDoubleWordsCommand,
                },
                new MenuItem
                {
                    Header = l.FindDoubleLines,
                    Command = vm.ShowFindDoubleLinesCommand,
                },
                new Separator(),
                new MenuItem
                {
                    Header = l.AddNameToNamesList,
                    Command = vm.ShowAddToNameListCommand,
                },
                new MenuItem
                {
                    Header = l.GetDictionaries,
                    Command = vm.ShowSpellCheckDictionariesCommand,
                },
                new MenuItem
                {
                    // The names/ignore/OCR-fix word lists are what spell check reads;
                    // upstream filed the editor for them under Options instead.
                    Header = l.WordLists,
                    Command = vm.ShowWordListsCommand,
                },
            }
        });

        var menuItemAudioTracks = new MenuItem
        {
            Header = l.AudioTracks,
        };
        menuItemAudioTracks.Bind(MenuItem.IsVisibleProperty, new Binding(nameof(vm.IsAudioTracksVisible)));
        vm.AudioTraksMenuItem = menuItemAudioTracks;

        // The old "Video > More" submenu is gone: it mixed transcoding, a view toggle and
        // two timing commands under a header that said nothing, three clicks deep and past
        // the depth where shortcuts used to be drawn. Its entries now sit in the block they
        // belong to (the two timing ones moved to Synchronization), each keeping the
        // IsVideoLoaded gate the submenu used to give them.
        menu.Items.Add(new MenuItem
        {
            Header = l.Video,
            Items =
            {
                new MenuItem
                {
                    Header = l.OpenVideo,
                    Command = vm.CommandVideoOpenCommand,
                },
                new MenuItem
                {
                    Header = l.OpenVideoFromUrl,
                    Command = vm.ShowVideoOpenFromUrlCommand,
                },
                new MenuItem
                {
                    Header = l.CloseVideoFile,
                    Command = vm.CommandVideoCloseCommand,
                },
                menuItemAudioTracks,
                new Separator(),
                new MenuItem
                {
                    Header = l.SpeechToText,
                    Command = vm.ShowSpeechToTextWhisperCommand,
                },
                new MenuItem
                {
                    Header = l.TextToSpeech,
                    Command = vm.ShowVideoTextToSpeechCommand,
                },
                new MenuItem
                {
                    Header = l.VideoOcr,
                    Command = vm.ShowVideoOcrCommand,
                },
                new Separator(),
                new MenuItem
                {
                    Header = l.GenerateBurnIn,
                    Command = vm.ShowVideoBurnInCommand,
                },
                new MenuItem
                {
                    Header = l.GenerateTransparent,
                    Command = vm.ShowVideoTransparentSubtitlesCommand,
                },
                new MenuItem
                {
                    Header = Se.Language.Video.GenerateBlankVideoDotDotDot,
                    Command = vm.VideoGenerateBlankCommand,
                },
                new MenuItem
                {
                    Header = Se.Language.Video.EmbedSubtitlesDotDotDot,
                    Command = vm.VideoEmbedCommand,
                },
                new MenuItem
                {
                    Header = Se.Language.Video.CutVideoDotDotDot,
                    Command = vm.VideoCutCommand,
                    [!MenuItem.IsVisibleProperty] = new Binding(nameof(vm.IsVideoLoaded)),
                },
                new MenuItem
                {
                    Header = Se.Language.Video.ReEncodeVideoForBetterSubtitlingDotDotDot,
                    Command = vm.VideoReEncodeCommand,
                    [!MenuItem.IsVisibleProperty] = new Binding(nameof(vm.IsVideoLoaded)),
                },
                new Separator(),
                new MenuItem
                {
                    Header = l.GenerateImportShotChanges,
                    Command = vm.ShowShotChangesSubtitlesCommand,
                },
                new MenuItem
                {
                    Header = l.ListShotChanges,
                    Command = vm.ShowShotChangesListCommand,
                    [!MenuItem.IsVisibleProperty] = new Binding(nameof(vm.ShowShotChangesListMenuItem)),
                },
                new Separator(),
                new MenuItem
                {
                    Header = l.UndockVideoControls,
                    Command = vm.VideoUndockControlsCommand,
                    [!MenuItem.IsVisibleProperty] = new Binding(nameof(vm.AreVideoControlsUndocked)) {  Converter = new InverseBooleanConverter() },
                },
                new MenuItem
                {
                    Header = l.ToggleSelectSubtitleWhilePlayingCurrentlyOn,
                    Command = vm.ToggleCurrentSubtitleWhilePlayingCommand,
                    [!MenuItem.IsVisibleProperty] = new Binding(nameof(vm.SelectCurrentSubtitleWhilePlaying)),
                },
                new MenuItem
                {
                    Header = l.ToggleSelectSubtitleWhilePlayingCurrentlyOff,
                    Command = vm.ToggleCurrentSubtitleWhilePlayingCommand,
                    [!MenuItem.IsVisibleProperty] = new Binding(nameof(vm.SelectCurrentSubtitleWhilePlaying)) {  Converter = new InverseBooleanConverter() },
                },
                new MenuItem
                {
                    Header = l.DockVideoControls,
                    Command = vm.VideoRedockControlsCommand,
                    [!MenuItem.IsVisibleProperty] = new Binding(nameof(vm.AreVideoControlsUndocked)),
                },
                new MenuItem
                {
                    // The menu-side label, which is what the macOS mirror already uses. The
                    // shortcut-dialog string upstream reused here is empty in some translations.
                    Header = l.WaveformToolbar,
                    Command = vm.ToggleIsWaveformToolbarVisibleCommand,
                    Icon = new Optris.Icons.Avalonia.Icon
                    {
                        Value = IconNames.CheckBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        [!Visual.IsVisibleProperty] = new Binding(nameof(vm.IsWaveformToolbarVisible)),
                    }
                },
                // Gated with the block it introduces, or the menu ends on a rule with nothing
                // under it whenever no video is loaded - which is how the app starts.
                new Separator
                {
                    [!Visual.IsVisibleProperty] = new Binding(nameof(vm.IsVideoLoaded)),
                },

                // Secondary subtitle shown on the player. Each of these already toggles on
                // whether a secondary subtitle is loaded, so the video gate they inherited
                // from the old submenu is re-applied with an AND rather than dropped.
                new MenuItem
                {
                    Header = Se.Language.Video.OpenSecondarySubtitleOnVideoPlayerDotDotDot,
                    Command = vm.OpenSecondarySubtitleCommand,
                    [!Visual.IsVisibleProperty] = new MultiBinding
                    {
                        Converter = BooleanAndConverter.Instance,
                        Bindings =
                        {
                            new Binding(nameof(vm.IsVideoLoaded)),
                            new Binding(nameof(vm.IsSubtitleSecondaryVisible)) { Converter = new InverseBooleanConverter() },
                        },
                    },
                },
                new MenuItem
                {
                    Header = Se.Language.Video.RemoveSecondarySubtitleOnVideoPlayer,
                    Command = vm.ClearSecondarySubtitleCommand,
                    [!Visual.IsVisibleProperty] = new MultiBinding
                    {
                        Converter = BooleanAndConverter.Instance,
                        Bindings =
                        {
                            new Binding(nameof(vm.IsVideoLoaded)),
                            new Binding(nameof(vm.IsSubtitleSecondaryVisible)),
                        },
                    },
                },
            },
        });

        menu.Items.Add(new MenuItem
        {
            Header = l.Synchronization,
            Items =
            {
                new MenuItem
                {
                    Header = l.AdjustAllTimes,
                    Command = vm.ShowSyncAdjustAllTimesCommand,
                },
                new MenuItem
                {
                    Header = l.VisualSync,
                    Command = vm.ShowVisualSyncCommand,
                },
                new MenuItem
                {
                    Header = l.PointSync,
                    Command = vm.ShowPointSyncCommand,
                },
                new MenuItem
                {
                    Header = l.PointSyncViaOther,
                    Command = vm.ShowPointSyncViaOtherCommand,
                },
                new MenuItem
                {
                    Header = l.ChangeFrameRate,
                    Command = vm.ShowSyncChangeFrameRateCommand,
                },
                new MenuItem
                {
                    Header = l.ChangeSpeed,
                    Command = vm.ShowSyncChangeSpeedCommand,
                },
                new Separator(),

                // Duration and gap tuning. These are timing work, but upstream filed them
                // in Tools, where the alphabetical sort also split the pairs apart.
                new MenuItem
                {
                    // Access key stripped: this label carries "_A", which "Adjust all times"
                    // above already owns. They were in different menus upstream, so the clash
                    // is new here, and a duplicate mnemonic stops the key from invoking at all.
                    Header = l.AdjustDurations.Replace("_", string.Empty),
                    Command = vm.ShowToolsAdjustDurationsCommand,
                },
                new MenuItem
                {
                    Header = l.ApplyDurationLimits,
                    Command = vm.ShowApplyDurationLimitsCommand,
                },
                new MenuItem
                {
                    Header = l.ApplyMinGap,
                    Command = vm.ShowApplyMinGapCommand,
                },
                new MenuItem
                {
                    Header = l.BridgeGaps,
                    Command = vm.ShowBridgeGapsCommand,
                },
                new MenuItem
                {
                    Header = l.BeautifyTimeCodes,
                    Command = vm.ShowBeautifyTimeCodesCommand,
                },
                new MenuItem
                {
                    Header = l.SnapAllTimesToFrames,
                    Command = vm.SnapAllTimesToFramesCommand,
                },
                // Both items below need a video, so the rule goes with them.
                new Separator
                {
                    [!Visual.IsVisibleProperty] = new Binding(nameof(vm.IsVideoLoaded)),
                },

                // Promoted out of Video > More: both are timing concepts, and at depth 3
                // they could never render their shortcut (see StampShortcuts).
                new MenuItem
                {
                    Header = Se.Language.Main.Menu.SetVideoOffset,
                    [!MenuItem.HeaderProperty] = new Binding(nameof(vm.SetVideoOffsetText)),
                    Command = vm.ShowVideoSetOffsetCommand,
                    [!MenuItem.IsVisibleProperty] = new Binding(nameof(vm.IsVideoLoaded)),
                },
                new MenuItem
                {
                    Header = l.SmpteTiming,
                    Command = vm.ToggleSmpteTimingCommand,
                    // Keeps the gate the old submenu gave it: ToggleSmpteTiming returns
                    // immediately with no message when no video is loaded, so an enabled
                    // entry there would be a silent no-op.
                    [!MenuItem.IsVisibleProperty] = new Binding(nameof(vm.IsVideoLoaded)),
                    Icon = new Optris.Icons.Avalonia.Icon
                    {
                        Value = IconNames.CheckBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        [!Visual.IsVisibleProperty] = new Binding(nameof(vm.IsSmpteTimingEnabled)),
                    }
                },
            }
        });

        menu.Items.Add(new MenuItem
        {
            Header = l.Translate,
            Items =
            {
                new MenuItem
                {
                    Header = l.AutoTranslate,
                    Command = vm.ShowAutoTranslateCommand,
                },
                new MenuItem
                {
                    Header = l.TranslateViaCopyPaste,
                    Command = vm.ShowTranslateViaCopyPasteCommand,
                },
                new Separator(),
                new MenuItem
                {
                    // Starting a translation, so it belongs here rather than in Tools.
                    Header = l.MakeEmptyTranslationFromCurrentSubtitle,
                    Command = vm.ToolsMakeEmptyTranslationFromCurrentSubtitleCommand,
                },
            }
        });

        menu.Items.Add(new MenuItem
        {
            Header = l.Options,
            Items =
            {
                new MenuItem
                {
                    Header = l.Settings,
                    Command = vm.CommandShowSettingsCommand,
                },
                new MenuItem
                {
                    Header = l.Shortcuts,
                    Command = vm.CommandShowSettingsShortcutsCommand,
                },
                new MenuItem
                {
                    Header = l.ChooseLanguage,
                    Command = vm.CommandShowSettingsLanguageCommand,
                },
                new Separator(),

                // These two had no menu home at all upstream - they existed only as toolbar
                // buttons, so switching the button off made the feature unreachable. There is
                // no menu-side label for them, so they borrow the Shortcuts dialog's, which
                // means no access key and a hand-added ellipsis (both open a dialog).
                new MenuItem
                {
                    Header = OrFallback(Se.Language.Options.Shortcuts.GeneralChooseLayout, "Choose layout") + "...",
                    Command = vm.CommandShowLayoutCommand,
                },
                new MenuItem
                {
                    Header = OrFallback(Se.Language.Options.Shortcuts.SourceView, "Source view") + "...",
                    Command = vm.ShowSourceViewCommand,
                },
            },
        });

        var menuItemAssaTools = new MenuItem
        {
            Header = l.AssaTools,
            [!MenuItem.IsVisibleProperty] = new Binding(nameof(vm.IsFormatAssa)),
        };

        // Ordered by how often they are reached, not alphabetically: the alphabetical build
        // put Styles - the entry most people open this menu for - dead last.
        foreach (var item in new List<Control>
        {
            new MenuItem
            {
                Header = l.AssaStyles,
                Command = vm.ShowAssaStylesCommand,
            },
            new MenuItem
            {
                Header = l.AssaProperties,
                Command = vm.ShowAssaPropertiesCommand,
            },
            new MenuItem
            {
                Header = l.AssaAttachments,
                Command = vm.ShowAssaAttachmentsCommand,
            },
            new Separator(),

            // Placement and effects on the selected lines
            new MenuItem
            {
                Header = l.AssaSetPosition,
                Command = vm.ShowAssaSetPositionCommand,
            },
            new MenuItem
            {
                Header = l.AssaApplyCustomOverrideTags,
                Command = vm.ShowAssaApplyCustomOverrideTagsCommand,
            },
            new MenuItem
            {
                Header = l.AssaApplyAdvancedEffects,
                Command = vm.ShowAssaApplyAdvancedEffectCommand,
            },
            new MenuItem
            {
                Header = l.AssaDraw,
                Command = vm.ShowAssaDrawCommand,
            },
            new Separator(),

            // Generators and document-wide settings
            new MenuItem
            {
                Header = l.AssaGenerateBackground,
                Command = vm.ShowAssaGenerateBackgroundCommand,
            },
            new MenuItem
            {
                Header = l.AssaProgressBar,
                Command = vm.ShowAssaGenerateProgressBarCommand,
            },
            new MenuItem
            {
                Header = l.AssaImageColorPicker,
                Command = vm.ShowAssaImageColorPickerCommand,
            },
            new MenuItem
            {
                Header = l.AssaChangeResolution,
                Command = vm.ShowAssaChangeResolutionCommand,
            },
            new Separator(),
            new MenuItem
            {
                Header = l.FilterLayersForDisplayDotDotDot,
                Command = vm.ShowPickLayerFilterCommand,
            },
        })
        {
            menuItemAssaTools.Items.Add(item);
        }

        menu.Items.Add(menuItemAssaTools);

        var menuItemSsaTools = new MenuItem
        {
            Header = l.SsaTools,
            [!MenuItem.IsVisibleProperty] = new Binding(nameof(vm.IsFormatSsa)),
        };
        menuItemSsaTools.Items.Add(new MenuItem
        {
            Header = l.AssaStyles,
            Command = vm.ShowSsaStylesCommand,
        });
        menuItemSsaTools.Items.Add(new MenuItem
        {
            Header = l.AssaProperties,
            Command = vm.ShowSsaPropertiesCommand,
        });
        menuItemSsaTools.Items.Add(new MenuItem
        {
            Header = l.AssaAttachments,
            Command = vm.ShowSsaAttachmentsCommand,
        });
        menu.Items.Add(menuItemSsaTools);

        // Added last so Help is the rightmost menu. The format-specific menus above are
        // conditional, so upstream's order put them after Help whenever they were shown.
        menu.Items.Add(new MenuItem
        {
            Header = l.HelpTitle,
            Items =
            {
                new MenuItem
                {
                    Header = l.CheckForUpdates,
                    Command = vm.ShowCheckForUpdatesCommand,
                },
                new Separator(),
                new MenuItem
                {
                    Header = l.Help,
                    Command = vm.ShowHelpCommand,
                },
                new MenuItem
                {
                    Header = l.About,
                    Command = vm.ShowAboutCommand,
                },
            }
        });
    }

    public static void UpdateRecentFiles(MainViewModel vm)
    {
        var files = Se.Settings.File.RecentFiles.Where(p => !string.IsNullOrEmpty(p.SubtitleFileName) && System.IO.File.Exists(p.SubtitleFileName)).ToList();
        vm.MenuReopen.Items.Clear();
        if (files.Count > 0)
        {
            foreach (var file in files)
            {
                var header = file.SubtitleFileName;

                if (!string.IsNullOrEmpty(file.SubtitleFileNameOriginal) && System.IO.File.Exists(file.SubtitleFileNameOriginal))
                {
                    header += " + ";
                    if (System.IO.Path.GetDirectoryName(file.SubtitleFileName) == System.IO.Path.GetDirectoryName(file.SubtitleFileNameOriginal))
                    {
                        header += System.IO.Path.GetFileName(file.SubtitleFileNameOriginal);
                    }
                    else
                    {
                        header += file.SubtitleFileNameOriginal;
                    }
                }

                // Trim the directory prefix with "…" when the path is too long so
                // the filename stays visible. Full path is still available via tooltip.
                var item = new MenuItem
                {
                    Header = new TextBlock
                    {
                        Text = header,
                        TextTrimming = TextTrimming.PrefixCharacterEllipsis,
                        MaxWidth = 600,
                    },
                    Command = vm.CommandFileReopenCommand,
                    CommandParameter = file,
                    [ToolTip.TipProperty] = header,
                };
                vm.MenuReopen.Items.Add(item);
            }

            vm.MenuReopen.Items.Add(new Separator());

            var clearItem = new MenuItem
            {
                Header = Se.Language.Main.Menu.ClearRecentFiles,
                Command = vm.CommandFileClearRecentFilesCommand,
            };
            vm.MenuReopen.Items.Add(clearItem);

            vm.MenuReopen.IsVisible = true;
        }
        else
        {
            vm.MenuReopen.IsVisible = false;
        }
    }

    /// <summary>
    /// (Re)builds the contents of the Plugins menu. Safe to call at runtime, e.g. after
    /// the plugin manager installs, removes, enables, or disables a plugin.
    /// </summary>
    public static void UpdatePluginsMenu(MainViewModel vm)
    {
        vm.MenuPlugins.Items.Clear();

        var enabledPlugins = vm.GetInstalledPlugins()
            .Where(p => !Se.Settings.Plugins.DisabledPluginNames.Contains(p.Manifest.Name))
            .OrderBy(p => p.Manifest.Name)
            .ToList();
        if (enabledPlugins.Count == 0)
        {
            vm.MenuPlugins.Items.Add(new MenuItem
            {
                Header = Se.Language.Plugins.NoPluginsInstalled,
                IsEnabled = false,
            });
        }
        else
        {
            foreach (var plugin in enabledPlugins)
            {
                vm.MenuPlugins.Items.Add(new MenuItem
                {
                    Header = plugin.Manifest.Name,
                    Command = vm.RunPluginCommand,
                    CommandParameter = plugin,
                    IsEnabled = plugin.CanRun,
                });
            }
        }

        vm.MenuPlugins.Items.Add(new Separator());
        vm.MenuPlugins.Items.Add(new MenuItem
        {
            Header = Se.Language.Plugins.ManagePlugins,
            Command = vm.ShowPluginManagerCommand,
        });
    }

    private static void DisplayShortcuts(Menu menu, MainViewModel vm)
    {
        List<ShortCut> availableShortcuts = ShortcutsMain.GetUsedShortcuts(vm);
        StampShortcuts(menu.Items.OfType<MenuItem>(), availableShortcuts);
    }

    /// <summary>
    /// Stamps the accelerator text on every menu item at any depth. This walked only two
    /// levels before, so items in a submenu of a submenu (File > Import/Export > *) never
    /// showed their shortcut even when one was bound - the key still fired, the hint was
    /// just invisible.
    /// </summary>
    private static void StampShortcuts(IEnumerable<MenuItem> items, List<ShortCut> availableShortcuts)
    {
        foreach (var item in items)
        {
            item.InputGesture = GetKeyGesture(availableShortcuts, item.Command);
            StampShortcuts(item.Items.OfType<MenuItem>(), availableShortcuts);
        }
    }

    private static KeyGesture? GetKeyGesture(List<ShortCut> availableShortcuts, System.Windows.Input.ICommand? command)
    {
        if (command is IRelayCommand relay)
        {
            foreach (var shortcut in availableShortcuts)
            {
                if (ReferenceEquals(shortcut.Action, relay))
                {
                    return ToKeyGesture(shortcut);
                }
            }
        }

        return null;
    }

    internal static KeyGesture? ToKeyGesture(ShortCut shortcut)
    {
        if (shortcut.Keys == null || shortcut.Keys.Count == 0)
        {
            return null;
        }

        var modifiers = KeyModifiers.None;
        Key? keyValue = null;

        foreach (var key in shortcut.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var k = key.Trim();
            var kLower = k.ToLowerInvariant();

            // Support combined tokens like "CtrlShift"
            if (kLower.Contains("ctrl") || kLower.Contains("control"))
            {
                modifiers |= KeyModifiers.Control;
            }
            if (kLower.Contains("shift"))
            {
                modifiers |= KeyModifiers.Shift;
            }
            if (kLower.Contains("alt"))
            {
                modifiers |= KeyModifiers.Alt;
            }
            if (kLower.Contains("win") || kLower.Contains("meta"))
            {
                // Map Win/Command to Meta so it renders appropriately across platforms
                modifiers |= KeyModifiers.Meta;
            }

            // If the whole token is not just a modifier, try parse as a key
            var isModifierOnly =
                kLower is "ctrl" or "control" or "shift" or "alt" or "win" or "meta" ||
                kLower == "ctrlshift" || kLower == "ctrlalt" || kLower == "shiftalt" ||
                kLower == "ctrlshiftalt" || kLower == "winshift" || kLower == "winctrl" ||
                kLower == "winalt" || kLower == "winctrlshift" || kLower == "metashift" ||
                kLower == "metactrl" || kLower == "metaalt" || kLower == "metactrlshift";

            if (!isModifierOnly && Enum.TryParse<Key>(k, ignoreCase: true, out var parsedKey))
            {
                keyValue = parsedKey;
            }
        }

        if (keyValue == null)
        {
            return null;
        }

        return new KeyGesture(keyValue.Value, modifiers);
    }
}