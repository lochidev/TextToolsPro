﻿using Android;
using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views.Accessibility;
using CommunityToolkit.Mvvm.Messaging;
using MauiApp1.Models;
using System.Security.Cryptography;
using System.Text.Json;

[Service(Exported = false, Label = "TextToolsPro", Permission = Manifest.Permission.BindAccessibilityService)]
[IntentFilter(new[] { "android.accessibilityservice.AccessibilityService" })]
[MetaData("android.accessibilityservice", Resource = "@xml/accessibility_service")]
public class MyAccessibilityService : AccessibilityService
{
    private Dictionary<string, Match> dict;
    private List<Var> globals;
    //private LinearLayout inputLayout;
    //private IWindowManager windowManager;
    private static readonly char[] separator = [' ', '\n', ','];
    //private static readonly char[] wordSeparator = [' ', /*'\n',*/ ','];

    public override void OnCreate()
    {
        base.OnCreate();
        dict = new();
        WeakReferenceMessenger.Default.Register<AcServiceMessage>(this, (r, m) =>
        {
            var cmd = m.Value.Item1;
            var item = m.Value.Item2;
            if (cmd == "Add")
            {
                if (!(string.IsNullOrEmpty(item.Trigger) || string.IsNullOrEmpty(item.Replace)))
                {
                    //dict.AddOrUpdate(item.Trigger, item,
                    //(key, oldValue) => item);
                    dict[item.Trigger] = item;
                }
            }
            else if (cmd == "Quit")
            {
                DisableSelf();
            }
            else if (cmd is not "_")
            {
                dict.Remove(item.Trigger, out var _);
            }
        });
        WeakReferenceMessenger.Default.Register<AcGlobalsMessage>(this, (r, m) =>
        {
            globals = m.Value;
        });
        try
        {
            if (File.Exists(AppSettings.DictPath))
            {
                using var stream = File.OpenRead(AppSettings.DictPath);
                dict = JsonSerializer.Deserialize<Dictionary<string, Match>>(stream);
            }
            else
                dict = new();
            if (File.Exists(AppSettings.GlobalVarsPath))
            {
                using var stream = File.OpenRead(AppSettings.GlobalVarsPath);
                globals = JsonSerializer.Deserialize<List<Var>>(stream);
            }
        }
        catch (Exception)
        {
        }
    }

    public override async void OnAccessibilityEvent(AccessibilityEvent e)
    {
        try
        {
            if (e.Source == null)
                return;
            if (e.Source.ClassName.Equals("android.widget.EditText"))
            {
                var Text = e.Text;
                if (Text != null)
                {
                    string og = Text[0].ToString();
                    //quick brown fox
                    var arr = og.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    bool send = false;

                    for (int wNum = 0; wNum < arr.Length; wNum++)
                    {
                        var text = arr[wNum];
                        if (dict.TryGetValue(text, out var match))
                        {
                            // echo, random, clipboard and date only supported
                            string replace = match.Replace;
                            if (match.Word)
                            { 
                                int index = 0;
                                for (int i = 0; i < wNum; i++)
                                {
                                    index += arr[0].Length;
                                }
                                var start = og.IndexOf(text, index);
                                //check the start
                                if (start != 0 && !separator.Contains(og[start - 1]))
                                {
                                    break;
                                }
                                //check the end
                                var end = start + text.Length;
                                if ((end) <= og.Length && !separator.Contains(og[end]))
                                {
                                    break;
                                }
                            }
                            if (globals is not null)
                            {
                                foreach (var item in globals)
                                {
                                    replace = await ParseItemAsync(item, replace);
                                }
                            }
                            if (match.Vars is not null && match.Vars.Count > 0)
                            {
                                foreach (var item in match.Vars)
                                {
                                    replace = await ParseItemAsync(item, replace);
                                }
                            }
                            if (replace is not null)
                            {
                                int index = 0;
                                for (int i = 0; i < wNum; i++)
                                {
                                    index += arr[i].Length;
                                }
                                var end = og[index..].Replace(text, replace);
                                og = og[..index] + end;
                                send = true;
                            }
                        }
                    }
                    if (send)
                    {
                        const string cursorStr = "$|$";
                        int startIndex = og.IndexOf(cursorStr);
                        Bundle args = new();
                        args.PutCharSequence(AccessibilityNodeInfo.ActionArgumentSetTextCharsequence, og);
                        Bundle cursorArgs = null;
                        if (startIndex != -1)
                        {
                            //og = og.Replace(cursorStr, " ");
                            cursorArgs = new Bundle();
                            cursorArgs.PutInt(AccessibilityNodeInfo.ActionArgumentSelectionStartInt, startIndex);
                            cursorArgs.PutInt(AccessibilityNodeInfo.ActionArgumentSelectionEndInt, startIndex + cursorStr.Length);
                        }
                        e.Source.PerformAction(Android.Views.Accessibility.Action.SetText, args);
                        if (cursorArgs is not null)
                        {
                            e.Source.PerformAction(Android.Views.Accessibility.Action.SetSelection, cursorArgs);
                        }
                    }
                }
            }

        }
        catch (Exception)
        {

        }
    }
    private async Task<string> ParseItemAsync(Var item, string replace)
    {
        try
        {
            if (item.Type is not null)
            {
                switch (item.Type)
                {
                    case "echo":
                        replace = replace.Replace(WrapName(item.Name), item.Params.Echo);
                        break;
                    case "random":
                        var choices = item.Params.Choices;
                        replace = replace.Replace(WrapName(item.Name), choices[RandomNumberGenerator.GetInt32(0, choices.Count)]);
                        break;
                    case "clipboard":
                        //if (Clipboard.Default.HasText)
                        {
                            var clip = await Clipboard.Default.GetTextAsync();
                            replace = replace.Replace(WrapName(item.Name), clip);
                        }
                        break;
                    case "date":
                        var param = item.Params;
                        var date = (DateTime.Now + TimeSpan.FromSeconds(param.Offset)).ToString(param.Format);
                        replace = replace.Replace(WrapName(item.Name), date);
                        break;
                    default:
                        break;
                }
            }
            return replace;
        }
        catch (Exception)
        {
            return null;
        }
    }
    private static string WrapName(string name)
    {
        return "{{" + name + "}}";
    }
    public override void OnInterrupt()
    {
        //throw new NotImplementedException();
    }
    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();
        WeakReferenceMessenger.Default.Send(new AcServiceMessage(("_", null)));
        // Create the input layout
        //inputLayout = new LinearLayout(this) { Orientation = Orientation.Vertical };  // Use native LinearLayout
        //inputLayout.SetBackgroundColor(Android.Graphics.Color.White); // Customize as needed

        //// Create the Entry for input
        //// Create the EditText for input
        //EditText inputEditText = new EditText(this) { Hint = "Type here..." };  // Use EditText
        //inputEditText.RequestFocus();  // Use RequestFocus instead of Focus
        //inputEditText.TextChanged += (sender, e) =>
        //{
        //    // Handle text changes, e.g., send text to other apps
        //};

        //// Add the EditText to the LinearLayout using AddView
        //inputLayout.AddView(inputEditText);

        //// Add the Entry to the LinearLayout using AddView

        //// Request the overlay permission (if not already granted)
        //if (Android.Provider.Settings.CanDrawOverlays(this))
        //{
        //    //Intent intent = new Intent(Android.Provider.Settings.ActionManageOverlayPermission,
        //    //        Android.Net.Uri.Parse("package:" + PackageName));
        //    //StartActivityForResult(intent, REQUEST_OVERLAY_PERMISSION);
        //}
        //else
        //{
        //    ShowInputBox();
        //}
    }

    //private void ShowInputBox()
    //{
    //    windowManager = (IWindowManager)GetSystemService(WindowService);
    //    WindowManagerLayoutParams param = new WindowManagerLayoutParams(
    //            WindowManagerLayoutParams.WrapContent,
    //            WindowManagerLayoutParams.WrapContent,
    //            WindowManagerTypes.AccessibilityOverlay,
    //            WindowManagerFlags.NotFocusable,
    //            Android.Graphics.Format.Translucent);
    //    windowManager.AddView(inputLayout, param);
    //}
    //public override bool OnUnbind(Intent intent)
    //{
    //    if (inputLayout != null)
    //    {
    //        windowManager.RemoveView(inputLayout);
    //    }
    //    return base.OnUnbind(intent);
    //}
}
