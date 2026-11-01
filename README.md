# CadDetailerAi

An offline AutoCAD 2019 drafting assistant for .NET Framework 4.7 (x64). It reads General Notes, checks model-space entities against editable rules, and asks a local Ollama model for draft actions. Draft output is always created on `AI_DRAFT`; existing drawing entities are not used as drawing targets.

## Build

1. Install Visual Studio with .NET Framework 4.7 and WPF targeting tools plus the .NET SDK.
2. Install AutoCAD 2019.
3. Build `CadDetailerAi.csproj`. The project references the managed API assemblies from `C:\Program Files\Autodesk\AutoCAD 2019`:

   ```powershell
   dotnet build .\CadDetailerAi.csproj -p:Platform=x64
   ```

   The project targets x64 and references `acdbmgd.dll`, `acmgd.dll`, and `accoremgd.dll` with Copy Local disabled. Newtonsoft.Json and PdfPig are restored by NuGet.
4. `rules.json`, `settings.json`, and the initial `columns.json` are copied to the build output directory beside the DLL.

The allowed layer list and text/geometry thresholds are project-specific examples; edit `rules.json` to match the drafting standard before checking drawings. PdfPig 0.1.8 is used because it supports the required .NET Framework 4.6 target.

## Load in AutoCAD

1. In AutoCAD 2019, enter `NETLOAD`.
2. Browse to `CadDetailerAi.dll` in the build output directory.
3. Run `READNOTES` and select the General Notes text. Confirm the notes printed in the command line.
4. Run `CHECKDWG` to inspect findings. Run `ZOOMTO` and enter a finding's handle to zoom to it.
5. Run `AIDRAFT`, enter a request, review warnings, then confirm with `Yes` to create the draft.
6. Use `ACCEPTDRAFT` to move draft objects to a chosen layer or `REJECTDRAFT` to erase them. `UNDO` reverses one draft batch.

`READNOTES`, `CHECKDWG`, `AIDRAFT`, `AICANCEL`, `IMPORTPDF`, `COLUMNS`, `ACCEPTDRAFT`, `REJECTDRAFT`, and `ZOOMTO` are the provided commands. For a large drawing, `AIDRAFT` requires an object selection to limit the context sent to the model.

## Column schedules

1. Run `READNOTES`, then `IMPORTPDF` and select a text-based column schedule PDF.
2. Review and edit the extracted values in the WPF grid. Missing values are shown as `?` and must be completed before drawing. Use the grid buttons to save/load `columns.json` beside the DLL.
3. Run `COLUMNS` and enter the From and To levels. The command displays cover, minimum-bar, units, and General Notes findings before asking whether to continue.
4. Column plan, schedule, and typical detail geometry is generated from code templates (no LLM) and placed only on `AI_DRAFT`. `S-COLS`, `S-SCHEDULE`, and `S-DETAIL` are created as destination-layer options; accept/reject remains the same single-target behavior as the other draft objects.

Scale, text size, layout origins, typical tie spacing, and lap length are editable in `settings.json`. Rule defaults for minimum column bars and cover are in `rules.json`. Scanned PDFs require OCR before import; this plugin deliberately does not infer numeric values from images. Column import, edits, matching, findings, and drafting actions are logged to `%TEMP%\CadDetailerAi.log`.

## Local Ollama

1. Install and start Ollama locally.
2. Run `ollama pull qwen2.5:7b` (or set another locally installed model in `settings.json`).
3. Keep the Ollama URL local, typically `http://localhost:11434`. The plugin sends no requests to external services.

Requests, compact drawing context, model replies, and errors are appended to `%TEMP%\CadDetailerAi.log`. Do not include confidential drawing data in requests if your local machine's temp folder is not suitably protected.
