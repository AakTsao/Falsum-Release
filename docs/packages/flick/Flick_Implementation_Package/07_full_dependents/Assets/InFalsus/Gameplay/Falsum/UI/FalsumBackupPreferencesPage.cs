using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// "Backup and restore" page: automatic-backup settings plus the list of
    /// backups for the chart package the dialog was opened for.
    ///
    /// The backup list is chart-scoped, so the page reads the package from the
    /// page host rather than assuming one is loaded: with no package it shows
    /// the "open a package first" hint and stops, which is what the old view
    /// did inline.
    /// </summary>
    internal static class FalsumBackupPreferencesPage
    {
        private static readonly CultureInfo Invariant =
            CultureInfo.InvariantCulture;

        public static void Build(
            VisualElement content,
            FalsumPreferences preferences,
            IFalsumPreferencesPageHost host)
        {
            var group = FalsumPreferencesWidgets.CreateGroup(
                L.Get("falsum.preferences.backup.group"));
            var enabled = new Toggle
            {
                value = preferences.Backup.Enabled,
            };
            enabled.AddToClassList("preferences-toggle");
            enabled.RegisterValueChangedCallback(
                evt =>
                {
                    preferences.Backup.Enabled = evt.newValue;
                });
            group.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.backup.enabled"),
                L.Get(
                    "falsum.preferences.backup.enabledDescription"),
                string.Empty,
                string.Empty,
                enabled,
                string.Empty));

            var intervals = new List<string>
            {
                L.Get("falsum.preferences.backup.everyMinute", ("count", 1)),
                L.Get("falsum.preferences.backup.everyMinute", ("count", 3)),
                L.Get("falsum.preferences.backup.everyMinute", ("count", 5)),
                L.Get("falsum.preferences.backup.everyMinute", ("count", 10)),
            };
            var seconds = new[] { 60, 180, 300, 600 };
            var intervalIndex = NearestIndex(
                seconds,
                preferences.Backup.IntervalSeconds);
            var interval = new DropdownField
            {
                choices = intervals,
                index = intervalIndex,
            };
            interval.AddToClassList("preferences-field");
            interval.tooltip = L.Get(
                "falsum.preferences.backup.intervalTooltip");
            interval.RegisterValueChangedCallback(
                evt =>
                {
                    preferences.Backup.IntervalSeconds =
                        seconds[Mathf.Clamp(
                            interval.index,
                            0,
                            seconds.Length - 1)];
                });
            group.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.backup.interval"),
                L.Get(
                    "falsum.preferences.backup.intervalDescription"),
                string.Empty,
                string.Empty,
                interval,
                "wide"));

            var retentionValues = new[] { 5, 10, 20, 50 };
            var retentionChoices = new List<string>
            {
                L.Get("falsum.preferences.backup.keepCount", ("count", 5)),
                L.Get("falsum.preferences.backup.keepCount", ("count", 10)),
                L.Get("falsum.preferences.backup.keepCount", ("count", 20)),
                L.Get("falsum.preferences.backup.keepCount", ("count", 50)),
            };
            var retention = new DropdownField
            {
                choices = retentionChoices,
                index = NearestIndex(
                    retentionValues,
                    preferences.Backup.RetentionCount),
            };
            retention.AddToClassList("preferences-field");
            retention.RegisterValueChangedCallback(
                evt =>
                {
                    preferences.Backup.RetentionCount =
                        retentionValues[Mathf.Clamp(
                            retention.index,
                            0,
                            retentionValues.Length - 1)];
                });
            group.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.backup.retention"),
                L.Get(
                    "falsum.preferences.backup.retentionDescription"),
                string.Empty,
                string.Empty,
                retention,
                "wide"));

            var open = new Button
            {
                text = L.Get("falsum.preferences.backup.openFolder"),
            };
            open.AddToClassList("preferences-inline-button");
            open.clicked += delegate
            {
                host.OpenBackupFolder();
            };
            group.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.backup.folder"),
                L.Get(
                    "falsum.preferences.backup.folderDescription"),
                string.Empty,
                string.Empty,
                open,
                string.Empty));
            content.Add(group);

            var restoreGroup = FalsumPreferencesWidgets.CreateGroup(
                L.Get("falsum.preferences.backup.restoreGroup"));
            var package = host.Package;
            if (package == null ||
                string.IsNullOrWhiteSpace(package.SourcePath))
            {
                var empty = new Label(
                    L.Get("falsum.error.metadata.packageRequired"));
                empty.AddToClassList("preferences-empty");
                restoreGroup.Add(empty);
                content.Add(restoreGroup);
                return;
            }
            List<FalsumBackupEntry> backups;
            try
            {
                backups = FalsumBackupService.ListBackups(
                    package.SourcePath,
                    package.Manifest.SongId);
            }
            catch (Exception exception)
            {
                host.ShowError(
                    L.Get(
                        "falsum.preferences.backup.readFailed",
                        ("error", exception.Message)));
                backups = new List<FalsumBackupEntry>();
            }
            if (backups.Count == 0)
            {
                var empty = new Label(
                    L.Get("falsum.preferences.backup.none"));
                empty.AddToClassList("preferences-empty");
                restoreGroup.Add(empty);
                content.Add(restoreGroup);
                return;
            }

            var listTitle = new Label(
                L.Get("falsum.preferences.backup.chooseHistory"));
            listTitle.AddToClassList("preferences-backup-list-title");
            restoreGroup.Add(listTitle);
            var listDescription = new Label(
                L.Get("falsum.preferences.backup.restoreDescription"));
            listDescription.AddToClassList(
                "preferences-backup-list-description");
            restoreGroup.Add(listDescription);

            var backupList = new ScrollView(ScrollViewMode.Vertical);
            backupList.AddToClassList("preferences-backup-list");
            backupList.horizontalScrollerVisibility =
                ScrollerVisibility.Hidden;
            backupList.verticalScrollerVisibility =
                ScrollerVisibility.Auto;
            restoreGroup.Add(backupList);

            var items = new List<VisualElement>();
            var selectedIndex = 0;
            Action<int> selectBackup = null;
            selectBackup = delegate(int index)
            {
                selectedIndex = Mathf.Clamp(index, 0, backups.Count - 1);
                for (var i = 0; i < items.Count; i++)
                {
                    items[i].EnableInClassList(
                        "is-selected",
                        i == selectedIndex);
                }
            };
            for (var i = 0; i < backups.Count; i++)
            {
                var index = i;
                var backup = backups[i];
                var item = new VisualElement
                {
                    focusable = true,
                    pickingMode = PickingMode.Position,
                    tooltip = backup.FilePath,
                };
                item.AddToClassList("preferences-backup-item");
                var itemContent = new VisualElement();
                itemContent.AddToClassList(
                    "preferences-backup-item-content");
                var itemHead = new VisualElement();
                itemHead.AddToClassList(
                    "preferences-backup-item-head");
                var difficulty = new Label(
                    DifficultyDisplayName(
                        Mathf.Clamp(backup.Difficulty, 0, 3)));
                difficulty.AddToClassList(
                    "preferences-backup-difficulty");
                itemHead.Add(difficulty);
                var time = new Label(
                    backup.ModifiedUtc
                        .ToLocalTime()
                        .ToString("yyyy-MM-dd HH:mm:ss", Invariant));
                time.AddToClassList("preferences-backup-time");
                itemHead.Add(time);
                itemContent.Add(itemHead);
                var fileName = new Label(
                    Path.GetFileName(backup.FilePath));
                fileName.AddToClassList("preferences-backup-file");
                itemContent.Add(fileName);
                item.Add(itemContent);
                var restore = new Button(
                    delegate
                    {
                        host.RestoreBackup(backups[index]);
                    })
                {
                    text = L.Get(
                        "falsum.preferences.backup.restoreAndWrite"),
                };
                restore.AddToClassList("preferences-inline-button");
                restore.AddToClassList(
                    "preferences-backup-item-restore");
                restore.RegisterCallback<ClickEvent>(
                    evt => evt.StopPropagation());
                item.Add(restore);
                item.RegisterCallback<ClickEvent>(
                    evt =>
                    {
                        evt.StopPropagation();
                        selectBackup(index);
                    });
                item.RegisterCallback<KeyDownEvent>(
                    evt =>
                    {
                        if (evt.keyCode != KeyCode.Return &&
                            evt.keyCode != KeyCode.KeypadEnter &&
                            evt.keyCode != KeyCode.Space)
                        {
                            return;
                        }
                        evt.StopImmediatePropagation();
                        selectBackup(index);
                    });
                backupList.Add(item);
                items.Add(item);
            }
            selectBackup(0);

            content.Add(restoreGroup);
        }

        private static int NearestIndex(int[] values, int target)
        {
            var best = 0;
            var bestDistance = int.MaxValue;
            for (var i = 0; i < values.Length; i++)
            {
                var distance = Mathf.Abs(values[i] - target);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }

        private static string DifficultyDisplayName(int index)
        {
            switch (index)
            {
                case 1:
                    return L.Get("falsum.difficulty.evo");
                case 2:
                    return L.Get("falsum.difficulty.ult");
                case 3:
                    return L.Get("falsum.difficulty.fbd");
                default:
                    return L.Get("falsum.difficulty.min");
            }
        }
    }
}
