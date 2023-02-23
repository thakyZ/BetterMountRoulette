﻿namespace BetterMountRoulette.UI;

using BetterMountRoulette.Config;

using ImGuiNET;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

internal sealed class ConfigWindow : IWindow
{
    private bool _isOpen;
    private readonly BetterMountRoulettePlugin _plugin;
    private string? _currentMountGroup;
    private ulong? _currentCharacter;

    public ConfigWindow(BetterMountRoulettePlugin plugin)
    {
        _plugin = plugin;
    }

    public override int GetHashCode()
    {
        return 0;
    }

    public override bool Equals(object? obj)
    {
        return obj is ConfigWindow;
    }

    public void Open()
    {
        _isOpen = true;
        Mounts.RefreshUnlocked();
    }

    public void Draw()
    {
        if (ImGui.Begin("Better Mount Roulette", ref _isOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (!BetterMountRoulettePlugin.ClientState.IsLoggedIn)
            {
                ImGui.Text("Please log in first");
            }
            else if (ImGui.BeginTabBar("settings"))
            {
                if (ImGui.BeginTabItem("General"))
                {
                    string? mountRouletteGroupName = _plugin.Configuration.MountRouletteGroup;
                    string? flyingRouletteGroupName = _plugin.Configuration.FlyingMountRouletteGroup;

                    SelectRouletteGroup(ref mountRouletteGroupName);
                    SelectRouletteGroup(ref flyingRouletteGroupName, isFlying: true);
                    ImGui.Text("For one of these to take effect, the selected group has to enable at least one mount.");

                    _plugin.Configuration.MountRouletteGroup = mountRouletteGroupName;
                    _plugin.Configuration.FlyingMountRouletteGroup = flyingRouletteGroupName;

                    // backwards compatibility
                    _plugin.Configuration.Enabled = (mountRouletteGroupName ?? flyingRouletteGroupName) is not null;
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Mount groups"))
                {
                    MountGroup mounts = SelectCurrentGroup();
                    DrawMountGroup(mounts);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Character Management"))
                {
                    DrawCharacterManagement();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        ImGui.End();

        if (!_isOpen)
        {
            BetterMountRoulettePlugin.SaveConfig(_plugin.Configuration);
            _plugin.WindowManager.Close(this);
        }
    }

    private void DrawCharacterManagement()
    {
        if (!ImGui.BeginListBox("Characters"))
        {
            return;
        }

        string selectedCharacterName = "NO CHARACTER SELECTED";
        foreach (CharacterConfig? character in _plugin.Configuration.CharacterConfigs.OrderBy(x => x.CharacterID))
        {
            StringBuilder sb = new(character.CharacterName);
            if (!string.IsNullOrWhiteSpace(character.CharacterWorld))
            {
                _ = sb.Append(CultureInfo.CurrentCulture, $" ({character.CharacterWorld})");
            }

            string text = sb.ToString();

            if (ImGui.Selectable(text, _currentCharacter == character.CharacterID))
            {
                Toggle(ref _currentCharacter, character.CharacterID);
            }

            if (_currentCharacter == character.CharacterID)
            {
                selectedCharacterName = text;
            }
        }

        ImGui.EndListBox();
        ImGui.BeginDisabled(_currentCharacter is null || _currentCharacter == BetterMountRoulettePlugin.ClientState.LocalContentId);

        if (ImGui.Button("Import"))
        {
            Debug.Assert(_currentCharacter is not null);
            ulong currentCharacter = _currentCharacter.Value;
            _plugin.WindowManager.Confirm(
                "Import settings?",
                $"Import settings from {selectedCharacterName}? This will overwrite all settings for this character!",
                ("Confirm", () => ImportFromCharacter(currentCharacter)),
                "Cancel");
        }

        if (ImGui.Button("Delete"))
        {
            Debug.Assert(_currentCharacter is not null);
            ulong currentCharacter = _currentCharacter.Value;
            _plugin.WindowManager.Confirm(
                "Delete settings?",
                $"Delete settings for {selectedCharacterName}? This action cannot be undone!",
                ("Confirm", () => DeleteCharacter(currentCharacter)),
                "Cancel");
        }

        ImGui.EndDisabled();
    }

    private void ImportFromCharacter(ulong characterID)
    {
        _plugin.WindowManager.Confirm("Import", $"Imported (not really - TODO {characterID})!", "OK");
    }

    private void DeleteCharacter(ulong characterID)
    {
        _plugin.WindowManager.Confirm("Deletion", $"Deleted (not really - TODO {characterID})!", "OK");
    }

    private static void Toggle<T>(ref T? field, T value) where T : struct
    {
        if (Equals(field, value))
        {
            field = null;
        }
        else
        {
            field = value;
        }
    }

    private MountGroup SelectCurrentGroup()
    {
        string? currentGroup = _currentMountGroup;
        _currentMountGroup ??= _plugin.Configuration.DefaultGroupName;

        SelectMountGroup(ref _currentMountGroup, "##currentgroup", 150);

        if (_currentMountGroup != currentGroup)
        {
            Mounts.GetInstance(_currentMountGroup)!.Filter(false, null, null);
        }

        int mode = 0;
        const int MODE_ADD = 1;
        const int MODE_EDIT = 2;
        const int MODE_DELETE = 3;

        ImGui.SameLine();
        mode = ImGui.Button("Add") ? MODE_ADD : mode;
        ImGui.SameLine();
        mode = ImGui.Button("Edit") ? MODE_EDIT : mode;
        ImGui.SameLine();
        ImGui.BeginDisabled(!_plugin.Configuration.Groups.Any());
        mode = ImGui.Button("Delete") ? MODE_DELETE : mode;
        ImGui.EndDisabled();

        currentGroup = _currentMountGroup;
        switch (mode)
        {
            case MODE_ADD:
                var dialog = new RenameItemDialog(_plugin.WindowManager, "Add a new group", "", AddGroup)
                {
                    NormalizeWhitespace = true
                };
                dialog.SetValidation(x => ValidateGroup(x, isNew: true), x => "A group with that name already exists.");
                _plugin.WindowManager.OpenDialog(dialog);
                break;
            case MODE_EDIT:
                dialog = new RenameItemDialog(
                    _plugin.WindowManager,
                    $"Rename {_currentMountGroup}",
                    _currentMountGroup,
                    (newName) => RenameMountGroup(_currentMountGroup, newName))
                {
                    NormalizeWhitespace = true
                };
                dialog.SetValidation(x => ValidateGroup(x, isNew: false), x => "Another group with that name already exists.");

                _plugin.WindowManager.OpenDialog(dialog);
                break;
            case MODE_DELETE:
                _plugin.WindowManager.Confirm(
                    "Confirm deletion of mount group",
                    $"Are you sure you want to delete {currentGroup}?\nThis action can NOT be undone.",
                    ("OK", () => DeleteMountGroup(currentGroup)),
                    "Cancel");
                break;
        }

        if (_currentMountGroup == _plugin.Configuration.DefaultGroupName)
        {
            return new DefaultMountGroup(_plugin.Configuration);
        }
        else
        {
            return _plugin.Configuration.Groups.First(x => x.Name == _currentMountGroup);
        }

        bool ValidateGroup(string newName, bool isNew)
        {
            HashSet<string> names = new(_plugin.Configuration.Groups.Select(x => x.Name), StringComparer.InvariantCultureIgnoreCase)
            {
                _plugin.Configuration.DefaultGroupName
            };

            if (!isNew)
            {
                _ = names.Remove(currentGroup);
            }

            return !names.Contains(newName);
        }
    }

    private void DeleteMountGroup(string name)
    {
        if (name == _plugin.Configuration.DefaultGroupName)
        {
            MountGroup? group = _plugin.Configuration.Groups.FirstOrDefault();
            if (group is null)
            {
                // can't delete the last group
                return;
            }

            _plugin.Configuration.DefaultGroupName = group.Name;
            _plugin.Configuration.EnabledMounts = group.EnabledMounts;
            _plugin.Configuration.IncludeNewMounts = group.IncludeNewMounts;
        }

        if (_plugin.Configuration.MountRouletteGroup == name)
        {
            _plugin.Configuration.MountRouletteGroup = _plugin.Configuration.DefaultGroupName;
        }

        if (_plugin.Configuration.FlyingMountRouletteGroup == name)
        {
            _plugin.Configuration.FlyingMountRouletteGroup = _plugin.Configuration.DefaultGroupName;
        }

        for (int i = 0; i < _plugin.Configuration.Groups.Count; ++i)
        {
            if (name == _plugin.Configuration.Groups[i].Name)
            {
                _plugin.Configuration.Groups.RemoveAt(i);
                break;
            }
        }

        Mounts.Remove(name);
        if (_currentMountGroup == name)
        {
            _currentMountGroup = null;
        }
    }

    private void RenameMountGroup(string currentMountGroup, string newName)
    {
        Configuration config = _plugin.Configuration;
        if (config.MountRouletteGroup == currentMountGroup)
        {
            config.MountRouletteGroup = newName;
        }

        if (config.FlyingMountRouletteGroup == currentMountGroup)
        {
            config.FlyingMountRouletteGroup = newName;
        }

        if (config.DefaultGroupName == currentMountGroup)
        {
            config.DefaultGroupName = newName;
        }
        else
        {
            MountGroup group = config.Groups.First(x => x.Name == currentMountGroup);
            group.Name = newName;
        }

        Mounts.Remove(currentMountGroup);
        if (_currentMountGroup == currentMountGroup)
        {
            _currentMountGroup = newName;
            Mounts.GetInstance(newName)!.Filter(false, null, null);
        }
    }

    private void AddGroup(string name)
    {
        Configuration config = _plugin.Configuration;
        config.Groups.Add(new MountGroup { Name = name });
        _currentMountGroup = name;
        Mounts inst = Mounts.GetInstance(name)!;
        inst.Filter(false, null, null);
        inst.Update(true);
    }

    private void DrawMountGroup(MountGroup group)
    {
        if (group is null)
        {
            ImGui.Text("Group is null!");
            return;
        }

        Mounts? mounts = Mounts.GetInstance(group.Name)!;

        if (mounts is null)
        {
            ImGui.Text($"Unable to load mounts for group {group.Name}!");
            return;
        }

        bool enableNewMounts = group.IncludeNewMounts;
        _ = ImGui.Checkbox("Enable new mounts on unlock", ref enableNewMounts);

        if (enableNewMounts != group.IncludeNewMounts)
        {
            group.IncludeNewMounts = enableNewMounts;
            mounts.UpdateUnlocked(enableNewMounts);
        }

        int pages = mounts.PageCount;
        if (pages == 0)
        {
            ImGui.Text("Please unlock at least one mount.");
        }
        else if (ImGui.BeginTabBar("mount_pages"))
        {
            for (int page = 1; page <= pages; page++)
            {
                if (ImGui.BeginTabItem($"{page}##mount_tab_{page}"))
                {
                    mounts.RenderItems(page);
                    mounts.Save(group);

                    int currentPage = page;
                    (bool Select, int? Page)? maybeInfo =
                        Buttons("Select all", "Unselect all", "Select page", "Unselect page") switch
                        {
                            0 => (true, default(int?)),
                            1 => (false, default(int?)),
                            2 => (true, page),
                            3 => (false, page),
                            _ => default((bool, int?)?),
                        };

                    if (maybeInfo is { } info)
                    {
                        string selectText = info.Select ? "select" : "unselect";
                        string pageInfo = (info.Page, info.Select) switch
                        {
                            (null, true) => "currently unselected mounts",
                            (null, false) => "currently selected mounts",
                            _ => "mounts on the current page",
                        };
                        _plugin.WindowManager.ConfirmYesNo(
                            "Are you sure?",
                            $"Do you really want to {selectText}select all {pageInfo}?",
                            () =>
                            {
                                mounts.Update(info.Select, info.Page);
                                mounts.Save(group);
                            });
                    }

                    ImGui.EndTabItem();
                }
            }

            ImGui.SameLine();

            ImGui.EndTabBar();
        }
    }

    private void SelectRouletteGroup(ref string? groupName, bool isFlying = false)
    {
        bool isEnabled = groupName is not null;
        _ = ImGui.Checkbox($"Enable for {(isFlying ? "Flying " : "")} Mount Roulette", ref isEnabled);
        if (isFlying && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(
                "Legacy action from when some mounts couldn't fly. " +
                "Not currently available in game without the help of external tools or via macro.");
        }

        if (isEnabled)
        {
            groupName ??= _plugin.Configuration.DefaultGroupName;

            ImGui.SameLine();
            SelectMountGroup(ref groupName, $"##roulettegroup_{(isFlying ? "f" : "g")}", 100);
        }
        else
        {
            groupName = null;
        }
    }

    private void SelectMountGroup(ref string groupName, string label, float? width = null)
    {
        if (width is float w)
        {
            ImGui.SetNextItemWidth(w);
        }

        if (ImGui.BeginCombo(label, groupName))
        {
            if (ImGui.Selectable(_plugin.Configuration.DefaultGroupName, groupName == _plugin.Configuration.DefaultGroupName))
            {
                groupName = _plugin.Configuration.DefaultGroupName;
            }

            foreach (MountGroup group in _plugin.Configuration.Groups)
            {
                if (ImGui.Selectable(group.Name, group.Name == groupName))
                {
                    groupName = group.Name;
                }
            }

            ImGui.EndCombo();
        }
    }

    private static int? Buttons(params string[] buttons)
    {
        int? result = null;
        for (int i = 0; i < buttons.Length; ++i)
        {
            if (i > 0)
            {
                ImGui.SameLine();
            }

            if (ImGui.Button(buttons[i]))
            {
                result = i;
            }
        }

        return result;
    }
}
