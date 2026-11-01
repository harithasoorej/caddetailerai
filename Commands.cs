using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace CadDetailerAi
{
    public sealed class Commands
    {
        private static List<string> _notes = new List<string>();
        private static List<ColumnData> _columnData = new List<ColumnData>();
        private static CancellationTokenSource _draftCancellation;

        [CommandMethod("IMPORTPDF")]
        public void ImportPdf()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            try
            {
                OpenFileDialog dialog = new OpenFileDialog
                {
                    Title = "Import column schedule PDF",
                    Filter = "PDF files (*.pdf)|*.pdf",
                    CheckFileExists = true,
                    Multiselect = false
                };
                if (dialog.ShowDialog() != true)
                    return;

                bool hasText;
                List<ColumnData> imported = ColumnPdfReader.Extract(dialog.FileName, out hasText);
                ColumnsStore.Log("Imported PDF " + dialog.FileName + "; text=" + hasText +
                    "; detected rows=" + imported.Count);
                if (!hasText)
                {
                    document.Editor.WriteMessage(
                        "\nThe PDF contains no extractable text. OCR is needed; no numbers were inferred.");
                    return;
                }
                if (imported.Count == 0)
                {
                    document.Editor.WriteMessage(
                        "\nText was found, but no column table with recognized headers was detected.");
                    return;
                }

                ColumnEditorWindow editor = new ColumnEditorWindow(imported);
                editor.ShowDialog();
                _columnData = editor.GetColumns();
                ColumnsStore.Log("User reviewed imported rows: " +
                    JsonConvert.SerializeObject(_columnData));
                document.Editor.WriteMessage("\nLoaded " + _columnData.Count +
                    " editable column row(s). Use the grid's Save button to write columns.json.");
            }
            catch (System.Exception ex)
            {
                ColumnsStore.Log("PDF import failed: " + ex);
                document.Editor.WriteMessage("\nIMPORTPDF failed: " + ex.Message);
            }
        }

        [CommandMethod("COLUMNS")]
        public void DrawColumns()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            try
            {
                if (_notes.Count == 0)
                {
                    document.Editor.WriteMessage(
                        "\nRun READNOTES first so the column rules can be checked against General Notes.");
                    return;
                }
                if (_columnData.Count == 0)
                    _columnData = ColumnsStore.Load();
                if (_columnData.Count == 0)
                {
                    document.Editor.WriteMessage("\nNo column data is loaded. Run IMPORTPDF first.");
                    return;
                }

                PromptStringOptions fromOptions = new PromptStringOptions("\nFrom level: ");
                fromOptions.AllowSpaces = false;
                PromptResult fromResult = document.Editor.GetString(fromOptions);
                if (fromResult.Status != PromptStatus.OK ||
                    String.IsNullOrWhiteSpace(fromResult.StringResult))
                    return;
                PromptStringOptions toOptions = new PromptStringOptions("\nTo level: ");
                toOptions.AllowSpaces = false;
                PromptResult toResult = document.Editor.GetString(toOptions);
                if (toResult.Status != PromptStatus.OK ||
                    String.IsNullOrWhiteSpace(toResult.StringResult))
                    return;

                string fromLevel = fromResult.StringResult.Trim();
                string toLevel = toResult.StringResult.Trim();
                List<ColumnData> selected = _columnData.FindAll(column =>
                    HasLevel(column.Levels, fromLevel) && HasLevel(column.Levels, toLevel));
                ColumnsStore.Log("COLUMNS requested " + fromLevel + " to " + toLevel +
                    "; matched " + selected.Count + " row(s).");
                if (selected.Count == 0)
                {
                    document.Editor.WriteMessage("\nNo columns match both requested levels.");
                    return;
                }

                PluginSettings settings = Settings.Load();
                List<string> incomplete = new List<string>();
                foreach (ColumnData column in selected)
                {
                    if (column.HasMissingValues())
                        incomplete.Add("Column " + column.Mark + " has missing values; fill every '?' in the grid.");
                }
                if (incomplete.Count > 0)
                {
                    foreach (string message in incomplete)
                        document.Editor.WriteMessage("\n" + message);
                    document.Editor.WriteMessage("\nDrawing blocked until selected column values are complete.");
                    ColumnsStore.Log("Drawing blocked: incomplete selected data.");
                    return;
                }

                List<string> validationErrors = ColumnDrawer.Validate(selected, settings);
                if (validationErrors.Count > 0)
                {
                    foreach (string error in validationErrors)
                        document.Editor.WriteMessage("\n[Invalid data] " + error);
                    ColumnsStore.Log("Drawing blocked: " + String.Join("; ", validationErrors));
                    return;
                }

                List<RuleFinding> findings = RuleChecker.CheckColumns(
                    selected, _notes, RuleChecker.LoadRules());
                ColumnsStore.Log("Rule findings: " +
                    JsonConvert.SerializeObject(findings, Formatting.None));
                if (findings.Count > 0)
                {
                    WriteFindings(document.Editor, findings);
                    PromptKeywordOptions confirmation = new PromptKeywordOptions(
                        "\nContinue with these column findings? [Yes/No] ", "Yes No");
                    confirmation.AllowNone = false;
                    confirmation.Keywords.Default = "No";
                    PromptResult confirm = document.Editor.GetKeywords(confirmation);
                    if (confirm.Status != PromptStatus.OK ||
                        !String.Equals(confirm.StringResult, "Yes", StringComparison.OrdinalIgnoreCase))
                    {
                        ColumnsStore.Log("User declined column drawing after rule findings.");
                        return;
                    }
                }
                else
                {
                    document.Editor.WriteMessage("\nCOLUMNS rule check: no findings.");
                }

                using (DocumentLock documentLock = document.LockDocument())
                {
                    document.StartUndoMark();
                    try
                    {
                        ColumnDrawer.Draw(document.Database, selected, settings);
                    }
                    finally
                    {
                        document.EndUndoMark();
                    }
                }
                ColumnsStore.Log("Drafted column plan, schedule, and typical detail for " +
                    String.Join(", ", selected.ConvertAll(column => column.Mark)) +
                    "; all new entities are on AI_DRAFT.");
                document.Editor.WriteMessage(
                    "\nColumn plan, schedule, and typical detail drafted on AI_DRAFT.");
            }
            catch (System.Exception ex)
            {
                ColumnsStore.Log("COLUMNS failed: " + ex);
                document.Editor.WriteMessage("\nCOLUMNS failed: " + ex.Message);
            }
        }

        [CommandMethod("READNOTES")]
        public void ReadNotes()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            try
            {
                PromptPointResult first = document.Editor.GetPoint(
                    "\nSelect first corner of GENERAL NOTES area (Enter for automatic search): ");
                ObjectId[] selected = null;
                if (first.Status == PromptStatus.OK)
                {
                    PromptCornerOptions corner = new PromptCornerOptions(
                        "\nSelect opposite corner: ", first.Value);
                    PromptPointResult second = document.Editor.GetCorner(corner);
                    if (second.Status == PromptStatus.OK)
                    {
                        TypedValue[] filterValues =
                        {
                            new TypedValue((int)DxfCode.Start, "TEXT,MTEXT")
                        };
                        SelectionFilter filter = new SelectionFilter(filterValues);
                        PromptSelectionResult selection = document.Editor.SelectWindow(
                            first.Value, second.Value, filter);
                        if (selection.Status == PromptStatus.OK)
                            selected = selection.Value.GetObjectIds();
                    }
                }

                if (selected == null || selected.Length == 0)
                {
                    document.Editor.WriteMessage(
                        "\nNo notes selected. Searching model space for 'GENERAL NOTES'...");
                    selected = FindGeneralNotes(document.Database);
                }

                _notes = NotesReader.Read(document.Database, selected);
                if (_notes.Count == 0)
                {
                    document.Editor.WriteMessage(
                        "\nNo numbered notes were found. Select the text more closely and run READNOTES again.");
                    return;
                }
                document.Editor.WriteMessage("\nLoaded " + _notes.Count + " General Notes:");
                for (int i = 0; i < _notes.Count; i++)
                    document.Editor.WriteMessage("\n" + (i + 1) + ". " + _notes[i]);
            }
            catch (System.Exception ex)
            {
                document.Editor.WriteMessage("\nREADNOTES failed: " + ex.Message);
            }
        }

        [CommandMethod("CHECKDWG")]
        public void CheckDrawing()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            try
            {
                List<RuleFinding> findings = RuleChecker.CheckDrawing(document.Database, _notes);
                WriteFindings(document.Editor, findings);
            }
            catch (System.Exception ex)
            {
                document.Editor.WriteMessage("\nCHECKDWG failed: " + ex.Message);
            }
        }

        [CommandMethod("ZOOMTO")]
        public void ZoomTo()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            PromptStringOptions options = new PromptStringOptions("\nFinding handle: ");
            options.AllowSpaces = false;
            PromptResult result = document.Editor.GetString(options);
            if (result.Status != PromptStatus.OK)
                return;
            try
            {
                long value;
                if (!Int64.TryParse(result.StringResult,
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    document.Editor.WriteMessage("\nInvalid object handle.");
                    return;
                }
                ObjectId id = document.Database.GetObjectId(false, new Handle(value), 0);
                using (Transaction transaction = document.Database.TransactionManager.StartTransaction())
                {
                    Entity entity = transaction.GetObject(id, OpenMode.ForRead, false) as Entity;
                    if (entity == null)
                    {
                        document.Editor.WriteMessage("\nThe handle does not identify a drawable entity.");
                        return;
                    }
                    Extents3d extents = entity.GeometricExtents;
                    ZoomToExtents(document.Editor, extents);
                    transaction.Commit();
                }
            }
            catch (System.Exception ex)
            {
                document.Editor.WriteMessage("\nZOOMTO failed: " + ex.Message);
            }
        }

        [CommandMethod("AIDRAFT")]
        public async Task AiDraft()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            if (_notes.Count == 0)
            {
                document.Editor.WriteMessage("\nRun READNOTES first so the project notes can be applied.");
                return;
            }

            PromptStringOptions requestOptions = new PromptStringOptions(
                "\nDescribe the drafting request: ");
            requestOptions.AllowSpaces = true;
            PromptResult requestResult = document.Editor.GetString(requestOptions);
            if (requestResult.Status != PromptStatus.OK ||
                String.IsNullOrWhiteSpace(requestResult.StringResult))
                return;

            string request = requestResult.StringResult.Trim();
            DrawingSnapshot snapshot = DrawingReader.Read(document.Database, null);
            PluginSettings settings = Settings.Load();
            if (snapshot.EntityCount > settings.LargeDrawingEntityThreshold)
            {
                PromptSelectionResult selection = document.Editor.GetSelection(
                    new PromptSelectionOptions { MessageForAdding = "\nSelect objects for AI context: " });
                if (selection.Status != PromptStatus.OK)
                {
                    document.Editor.WriteMessage(
                        "\nLarge drawing: select objects to limit the AI context, then run AIDRAFT again.");
                    return;
                }
                snapshot = DrawingReader.Read(document.Database, selection.Value.GetObjectIds());
            }

            string drawingJson = DrawingReader.ToCompactJson(snapshot);
            Log("REQUEST\n" + request + "\nDRAWING\n" + drawingJson);
            _draftCancellation = new CancellationTokenSource();
            try
            {
                ILlmClient client = new OllamaClient(settings);
                document.Editor.WriteMessage("\nContacting local Ollama. Run AICANCEL to cancel.");
                DraftResponse response = await client.CreateDraftAsync(request,
                    String.Join("\n", _notes), drawingJson, _draftCancellation.Token);
                Log("REPLY\n" + JsonConvert.SerializeObject(response, Formatting.Indented));

                List<string> warnings = new List<string>(response.Warnings);
                List<DraftAction> validActions = ActionDrawer.Validate(response.Actions, warnings);
                warnings.AddRange(RuleChecker.CheckActions(validActions, RuleChecker.LoadRules())
                    .ConvertAll(f => f.Severity + ": " + f.Message));
                if (warnings.Count > 0)
                {
                    document.Editor.WriteMessage("\nWarnings:");
                    foreach (string warning in warnings)
                        document.Editor.WriteMessage("\n - " + warning);
                }
                if (validActions.Count == 0)
                {
                    document.Editor.WriteMessage("\nNo valid actions to draw.");
                    return;
                }

                PromptKeywordOptions confirmation = new PromptKeywordOptions(
                    "\nContinue? [Yes/No] ", "Yes No");
                confirmation.AllowNone = false;
                confirmation.Keywords.Default = "No";
                PromptResult confirm = document.Editor.GetKeywords(confirmation);
                if (confirm.Status != PromptStatus.OK ||
                    !String.Equals(confirm.StringResult, "Yes", StringComparison.OrdinalIgnoreCase))
                {
                    document.Editor.WriteMessage("\nDraft not drawn.");
                    return;
                }

                using (DocumentLock documentLock = document.LockDocument())
                {
                    document.StartUndoMark();
                    try
                    {
                        ActionDrawer.Draw(document.Database, validActions);
                    }
                    finally
                    {
                        document.EndUndoMark();
                    }
                }
                document.Editor.WriteMessage("\nDraft created on AI_DRAFT. Use ACCEPTDRAFT or REJECTDRAFT.");
            }
            catch (OperationCanceledException)
            {
                document.Editor.WriteMessage("\nAI draft request cancelled.");
            }
            catch (System.Exception ex)
            {
                Log("ERROR\n" + ex);
                document.Editor.WriteMessage("\nAIDRAFT failed: " + ex.Message);
            }
            finally
            {
                if (_draftCancellation != null)
                {
                    _draftCancellation.Dispose();
                    _draftCancellation = null;
                }
            }
        }

        [CommandMethod("AICANCEL")]
        public void CancelAiDraft()
        {
            if (_draftCancellation == null)
            {
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage(
                    "\nNo active AI draft request.");
                return;
            }
            _draftCancellation.Cancel();
            Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage(
                "\nCancellation requested.");
        }

        [CommandMethod("ACCEPTDRAFT")]
        public void AcceptDraft()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            PromptStringOptions options = new PromptStringOptions("\nTarget layer name: ");
            options.AllowSpaces = false;
            PromptResult result = document.Editor.GetString(options);
            if (result.Status != PromptStatus.OK)
                return;
            try
            {
                using (DocumentLock documentLock = document.LockDocument())
                {
                    int count = DraftManager.Accept(document.Database, result.StringResult);
                    Log("ACCEPTDRAFT moved " + count + " AI_DRAFT object(s) to " +
                        result.StringResult + ".");
                    document.Editor.WriteMessage("\nAccepted " + count + " draft object(s).");
                }
            }
            catch (System.Exception ex)
            {
                document.Editor.WriteMessage("\nACCEPTDRAFT failed: " + ex.Message);
            }
        }

        [CommandMethod("REJECTDRAFT")]
        public void RejectDraft()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            try
            {
                using (DocumentLock documentLock = document.LockDocument())
                {
                    int count = DraftManager.Reject(document.Database);
                    Log("REJECTDRAFT erased " + count + " AI_DRAFT object(s).");
                    document.Editor.WriteMessage("\nRejected " + count + " draft object(s).");
                }
            }
            catch (System.Exception ex)
            {
                document.Editor.WriteMessage("\nREJECTDRAFT failed: " + ex.Message);
            }
        }

        private static ObjectId[] FindGeneralNotes(Database database)
        {
            List<ObjectId> ids = new List<ObjectId>();
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = (BlockTable)transaction.GetObject(
                    database.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(
                    blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                string notesLayer = null;
                List<ObjectId> textIds = new List<ObjectId>();
                foreach (ObjectId id in modelSpace)
                {
                    Entity entity = transaction.GetObject(id, OpenMode.ForRead, false) as Entity;
                    string text = entity is DBText ? ((DBText)entity).TextString :
                        entity is MText ? ((MText)entity).Contents : null;
                    if (String.IsNullOrEmpty(text))
                        continue;
                    textIds.Add(id);
                    if (text.IndexOf("GENERAL NOTES", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ids.Add(id);
                        notesLayer = entity.Layer;
                    }
                }
                if (notesLayer != null)
                {
                    foreach (ObjectId id in textIds)
                    {
                        Entity entity = (Entity)transaction.GetObject(id, OpenMode.ForRead, false);
                        if (String.Equals(entity.Layer, notesLayer, StringComparison.OrdinalIgnoreCase) &&
                            !ids.Contains(id))
                            ids.Add(id);
                    }
                }
                transaction.Commit();
            }
            return ids.ToArray();
        }

        private static bool HasLevel(string levels, string requested)
        {
            if (String.IsNullOrWhiteSpace(levels))
                return false;
            string[] tokens = System.Text.RegularExpressions.Regex.Split(
                levels.Trim(), @"[^A-Za-z0-9]+");
            foreach (string token in tokens)
            {
                if (String.Equals(token, requested, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void WriteFindings(Editor editor, IList<RuleFinding> findings)
        {
            if (findings.Count == 0)
            {
                editor.WriteMessage("\nCHECKDWG: no findings.");
                return;
            }
            editor.WriteMessage("\nCHECKDWG findings (" + findings.Count + "):");
            foreach (RuleFinding finding in findings)
                editor.WriteMessage("\n[" + finding.Severity + "] " + finding.Message +
                    " | Handle: " + finding.Handle);
        }

        private static void ZoomToExtents(Editor editor, Extents3d extents)
        {
            double width = Math.Max(extents.MaxPoint.X - extents.MinPoint.X, 1.0);
            double height = Math.Max(extents.MaxPoint.Y - extents.MinPoint.Y, 1.0);
            ViewTableRecord view = editor.GetCurrentView();
            view.CenterPoint = new Point2d(
                (extents.MinPoint.X + extents.MaxPoint.X) / 2.0,
                (extents.MinPoint.Y + extents.MaxPoint.Y) / 2.0);
            view.Width = width * 1.25;
            view.Height = height * 1.25;
            editor.SetCurrentView(view);
            view.Dispose();
        }

        private static void Log(string text)
        {
            string path = Path.Combine(Path.GetTempPath(), "CadDetailerAi.log");
            File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                Environment.NewLine + text + Environment.NewLine + Environment.NewLine);
        }
    }
}
