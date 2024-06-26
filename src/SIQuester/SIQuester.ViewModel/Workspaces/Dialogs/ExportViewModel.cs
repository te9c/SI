﻿using SIPackages.Core;
using SIQuester.Model;
using SIQuester.ViewModel.PlatformSpecific;
using SIQuester.ViewModel.Properties;
using System.Diagnostics;
using System.Text;
using System.Windows.Input;
using Utils.Commands;

namespace SIQuester.ViewModel;

/// <summary>
/// Represents a document export view model.
/// </summary>
public sealed class ExportViewModel : WorkspaceViewModel
{
    private readonly QDocument _source;

    public override string Header => $"{_source.Document.Package.Name}: {Resources.ExportAndPrint}";

    public ICommand Save { get; private set; }
    public ICommand Print { get; private set; }

    private IFlowDocumentWrapper? _documentWrapper = null;

    private object? _document = null;

    public object? Document
    {
        get => _document;
        set
        {
            if (_document != value)
            {
                _document = value;
                OnPropertyChanged();
            }
        }
    }

    private ExportFormats _format = ExportFormats.Dinabank;

    public ExportFormats Format
    {
        get => _format;
        set
        {
            if (_format != value)
            {
                _format = value;
                OnPropertyChanged();

                BuildDocument();
            }
        }
    }

    private void BuildDocument()
    {
        try
        {
            _documentWrapper = PlatformManager.Instance.BuildDocument(_source.Document, _format);
            Document = _documentWrapper.GetDocument();
        }
        catch (Exception exc)
        {
            OnError(exc);
        }
    }

    public ExportViewModel(QDocument source, ExportFormats format)
    {
        _source = source;
        _format = format;

        Save = new SimpleCommand(Save_Executed);
        Print = new SimpleCommand(Print_Executed);

        BuildDocument();
    }

    private async void Save_Executed(object? arg)
    {
        try
        {
            string filename = _source.FileName.Replace(".", "-");

            var filter = new Dictionary<string, string>
            {
                [Resources.TextFiles] = "txt",
                [Resources.HtmlFiles] = "html",
                [Resources.DocxFiles] = "docx",
                [Resources.RtfFiles] = "rtf",
                [Resources.XpsFiles] = "xps"
            };

            int index = 0;

            if (PlatformManager.Instance.ShowExportUI(Resources.Export, filter, ref filename, ref index, out Encoding encoding, out bool start))
            {
                OnClosed();
                await ExportAsync(filename, index, encoding);
                
                if (start)
                {
                    try
                    {
                        Process.Start(filename);
                    }
                    catch (Exception exc)
                    {
                        OnError(exc);
                    }
                }
            }
        }
        catch (Exception exc)
        {
            OnError(exc);
        }
    }

    internal async Task ExportAsync(string filename, int index, Encoding encoding)
    {
        switch (index)
        {
            case 2:
                ExportHtml(filename);
                break;

            case 3:
                _documentWrapper.ExportDocx(filename);
                break;

            case 4:
                ExportRtf(filename);
                break;

            case 5:
                _documentWrapper.ExportXps(filename);
                break;

            default:
                ExportTxt(filename, encoding);
                break;
        }

        await ExportMediaAsync(filename);
    }

    /// <summary>
    /// Выгрузить медиа в подпапку
    /// </summary>
    /// <param name="filename">Имя основного файла для экспорта</param>
    private async Task ExportMediaAsync(string filename)
    {
        var extMediaNew = new List<(MediaInfo, string)>();

        foreach (var round in _source.Package.Rounds)
        {
            foreach (var theme in round.Themes)
            {
                foreach (var quest in theme.Questions)
                {
                    foreach (var content in quest.Model.GetContent())
                    {
                        if (content.IsRef)
                        {
                            var mediaInfo = _source.Document.TryGetMedia(content);

                            if (mediaInfo.HasValue && mediaInfo.Value.HasStream)
                            {
                                extMediaNew.Add((mediaInfo.Value, content.Value));
                            }
                        }
                    }
                }
            }
        }

        if (extMediaNew.Any())
        {
            var name = Path.GetFileNameWithoutExtension(filename);
            var folder = Path.Combine(Path.GetDirectoryName(filename), name + "_Media");
            Directory.CreateDirectory(folder);

            foreach (var (media, fileName) in extMediaNew)
            {
                var stream = media.Stream;

                if (stream == null)
                {
                    continue;
                }

                using (stream)
                {
                    var file = Path.Combine(folder, fileName);
                    using var fs = File.Open(file, FileMode.Create, FileAccess.Write);
                    await stream.CopyToAsync(fs);
                }
            }
        }
    }

    private void ExportTxt(string filename, Encoding encoding) =>
        _documentWrapper.WalkAndSave(filename, encoding, sr => sr.WriteLine(), (sr, text) => sr.Write(text));

    private void ExportHtml(string filename) =>
        _documentWrapper.WalkAndSave(
            filename,
            Encoding.UTF8,
            sr => sr.Write("<br>"),
            (sr, text) => sr.Write(text),
            sr => sr.Write(
                string.Format("<!DOCTYPE html><html><head><title>{0}</title></head><body>",
                _source.Document.Package.Name)),
            sr => sr.Write("</body></html>"));

    private void ExportRtf(string filename) =>
        // RTF does not support Unicode!
        _documentWrapper.WalkAndSave(
            filename,
            Encoding.GetEncoding(1251),
            sr => sr.Write(@"\par "),
            (sr, text) => sr.Write(text.Replace(@"\", @"\\")),
            sr =>
            {
                sr.Write("{\\rtf1\\ansi\\ansicpg1251\\deff0\\deflang1049");
                sr.Write("{\\fonttbl{\\f0\\fnil\\fcharset204{\\*\\fname Times New Roman;}Times New Roman;}}");
                sr.Write("\r\n\\viewkind4\\uc1\\pard\\f0\\fs24 ");
            },
            sr => sr.Write("\r\n}\r\n"));

    private void Print_Executed(object? arg)
    {
        if (_documentWrapper.Print())
        {
            OnClosed();
        }
    }
}
