using System;
using System.Collections.Generic;

namespace VRMCast.Core.Localization
{
    /// <summary>
    /// All UI strings, keyed. Both languages must define exactly the same keys (enforced by tests).
    /// Keys are grouped by screen area; "{0}" style placeholders follow string.Format.
    /// </summary>
    public static class LocalizationTable
    {
        public static IReadOnlyDictionary<string, string> For(AppLanguage language)
        {
            switch (language)
            {
                case AppLanguage.En: return English;
                default: return TraditionalChinese;
            }
        }

        public static IEnumerable<string> Keys => English.Keys;

        public static readonly IReadOnlyDictionary<string, string> TraditionalChinese = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Header / status bar
            ["header.subtitle"] = "渲染基礎（MVP-A）",
            ["header.profile"] = "設定檔：預設",
            ["header.language"] = "語言",
            ["status.face"] = "臉部：關",
            ["status.audio"] = "音訊：關",
            ["status.render"] = "渲染 {0} fps",
            ["status.outputOn"] = "輸出：開",
            ["status.outputOff"] = "輸出：關",
            ["status.resolution"] = "{0}×{1} | {2} FPS",

            // Model section
            ["section.model"] = "模型",
            ["model.none"] = "尚未載入模型",
            ["model.hint"] = "請載入 .vrm 檔案開始使用。",
            ["model.load"] = "載入 VRM",
            ["model.loading"] = "載入中…",
            ["model.unload"] = "卸載",
            ["model.status"] = "{0}{1} · {2} 個表情 · SpringBone {3}",
            ["model.author"] = " · {0}",
            ["model.springOn"] = "有",
            ["model.springOff"] = "無",
            ["model.loaded"] = "已載入 {0}",
            ["import.title"] = "進階匯入",
            ["import.version"] = "VRM 版本",
            ["import.auto"] = "自動偵測",
            ["import.force0"] = "強制 VRM 0.x",
            ["import.force1"] = "強制 VRM 1.0",
            ["import.retry"] = "以此版本重試",

            // Framing section
            ["section.framing"] = "構圖",
            ["framing.preset"] = "預設",
            ["framing.face"] = "臉部",
            ["framing.bust"] = "胸像",
            ["framing.halfBody"] = "半身",
            ["framing.fullBody"] = "全身",
            ["framing.fov"] = "視角",
            ["framing.resetCamera"] = "重設鏡頭",
            ["framing.resetOrientation"] = "重設方向",
            ["framing.reframe"] = "重新對準模型",
            ["framing.hint"] = "預覽區：滾輪＝縮放，拖曳＝環繞，右鍵／中鍵或 Shift＋拖曳＝平移",

            // Background section
            ["section.background"] = "背景",
            ["background.mode"] = "模式",
            ["background.solid"] = "純色",
            ["background.image"] = "圖片",
            ["background.chroma"] = "去背綠幕",
            ["background.transparent"] = "透明",
            ["background.keyColor"] = "去背色",
            ["background.color"] = "顏色",
            ["background.chooseImage"] = "選擇圖片…",
            ["background.clearImage"] = "清除",
            ["background.noImage"] = "尚未選擇圖片",
            ["background.fit"] = "填滿方式",
            ["fit.fill"] = "填滿",
            ["fit.fit"] = "等比置入",
            ["fit.stretch"] = "拉伸",
            ["background.colorFormat"] = "請以 #RRGGBB 格式輸入顏色。",

            // Output section
            ["section.output"] = "輸出",
            ["output.quality"] = "品質",
            ["output.recommended"] = "建議",
            ["output.hint"] = "虛擬攝影機將在 MVP-E 提供。「開始輸出」目前只啟動除錯用的接收端。",
            ["output.start"] = "開始輸出",
            ["output.stop"] = "停止輸出",

            // Diagnostics section
            ["section.diagnostics"] = "診斷",
            ["diag.render"] = "渲染：{0} fps（{1} ms）",
            ["diag.output"] = "輸出：{0}×{1} @ {2} · 目標 {3}",
            ["diag.avatar"] = "模型：{0} [{1}]",
            ["diag.copy"] = "複製診斷資訊",
            ["diag.copied"] = "診斷資訊已複製到剪貼簿。",

            // File picker
            ["picker.loadVrm"] = "載入 VRM",
            ["picker.chooseImage"] = "選擇背景圖片",
            ["picker.up"] = "上一層",
            ["picker.home"] = "個人",
            ["picker.desktop"] = "桌面",
            ["picker.downloads"] = "下載項目",
            ["picker.documents"] = "文件",
            ["picker.cancel"] = "取消",
            ["picker.open"] = "開啟",
            ["picker.folderUnreadable"] = "無法讀取這個資料夾。",
            ["picker.fileMissing"] = "找不到這個檔案。",
            ["picker.wrongType"] = "這裡不支援這種檔案類型。",

            // VRM versions
            ["vrm.0x"] = "VRM 0.x",
            ["vrm.10"] = "VRM 1.0",
            ["vrm.unknown"] = "未知版本",

            // Errors (keys referenced from VrmLoadErrors and BackgroundService)
            ["error.noFile"] = "尚未選擇檔案。",
            ["error.fileMissing"] = "找不到檔案，可能已被移動或刪除。",
            ["error.unreadable"] = "無法讀取檔案，請確認你有開啟它的權限。",
            ["error.notVrm"] = "這不是有效的 VRM 檔案。",
            ["error.unsupportedGlb"] = "這個檔案使用了不支援的 glTF 容器版本。",
            ["error.metadata"] = "無法讀取 VRM 中繼資料。",
            ["error.noHumanoid"] = "這個模型沒有相容的人形骨架定義。",
            ["error.springBone"] = "模型已載入，但 SpringBone 資料無效，已停用物理效果。",
            ["error.importer"] = "無法載入模型。檔案可能已損壞，或使用了此版本不支援的功能。",
            ["error.cancelled"] = "已取消載入。",
            ["error.versionMismatch"] = "偵測到此檔案為 {0}，無法以 {1} 載入。",
            ["error.imageMissing"] = "找不到背景圖片。",
            ["error.imageUnreadable"] = "無法讀取背景圖片。",
            ["error.imageFormat"] = "不支援這種圖片格式，請使用 PNG 或 JPEG。",
            ["error.imageLoad"] = "無法載入圖片。",
        };

        public static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Header / status bar
            ["header.subtitle"] = "Render foundation (MVP-A)",
            ["header.profile"] = "Profile: Default",
            ["header.language"] = "Language",
            ["status.face"] = "Face: off",
            ["status.audio"] = "Audio: off",
            ["status.render"] = "Render {0} fps",
            ["status.outputOn"] = "Output: on",
            ["status.outputOff"] = "Output: off",
            ["status.resolution"] = "{0}×{1} | {2} FPS",

            // Model section
            ["section.model"] = "MODEL",
            ["model.none"] = "No avatar loaded",
            ["model.hint"] = "Load a .vrm file to begin.",
            ["model.load"] = "Load VRM",
            ["model.loading"] = "Loading…",
            ["model.unload"] = "Unload",
            ["model.status"] = "{0}{1} · {2} expressions · SpringBone {3}",
            ["model.author"] = " · {0}",
            ["model.springOn"] = "on",
            ["model.springOff"] = "none",
            ["model.loaded"] = "Loaded {0}",
            ["import.title"] = "Advanced import",
            ["import.version"] = "VRM Version",
            ["import.auto"] = "Auto Detect",
            ["import.force0"] = "Force VRM 0.x",
            ["import.force1"] = "Force VRM 1.0",
            ["import.retry"] = "Retry with this version",

            // Framing section
            ["section.framing"] = "FRAMING",
            ["framing.preset"] = "Preset",
            ["framing.face"] = "Face",
            ["framing.bust"] = "Bust",
            ["framing.halfBody"] = "Half Body",
            ["framing.fullBody"] = "Full Body",
            ["framing.fov"] = "FOV",
            ["framing.resetCamera"] = "Reset Camera",
            ["framing.resetOrientation"] = "Reset Orientation",
            ["framing.reframe"] = "Reframe to Avatar",
            ["framing.hint"] = "Preview: scroll = zoom, drag = orbit, right/middle or Shift+drag = pan",

            // Background section
            ["section.background"] = "BACKGROUND",
            ["background.mode"] = "Mode",
            ["background.solid"] = "Solid Color",
            ["background.image"] = "Image",
            ["background.chroma"] = "Chroma Key",
            ["background.transparent"] = "Transparent",
            ["background.keyColor"] = "Key color",
            ["background.color"] = "Color",
            ["background.chooseImage"] = "Choose Image…",
            ["background.clearImage"] = "Clear",
            ["background.noImage"] = "No image selected",
            ["background.fit"] = "Fit",
            ["fit.fill"] = "Fill",
            ["fit.fit"] = "Fit",
            ["fit.stretch"] = "Stretch",
            ["background.colorFormat"] = "Enter a color as #RRGGBB.",

            // Output section
            ["section.output"] = "OUTPUT",
            ["output.quality"] = "Quality",
            ["output.recommended"] = "Recommended",
            ["output.hint"] = "The virtual camera arrives in MVP-E. Start Output runs the debug consumer.",
            ["output.start"] = "Start Output",
            ["output.stop"] = "Stop Output",

            // Diagnostics section
            ["section.diagnostics"] = "DIAGNOSTICS",
            ["diag.render"] = "Render: {0} fps ({1} ms)",
            ["diag.output"] = "Output: {0}×{1} @ {2} · target {3}",
            ["diag.avatar"] = "Avatar: {0} [{1}]",
            ["diag.copy"] = "Copy Diagnostics",
            ["diag.copied"] = "Diagnostics copied to the clipboard.",

            // File picker
            ["picker.loadVrm"] = "Load VRM",
            ["picker.chooseImage"] = "Choose background image",
            ["picker.up"] = "Up",
            ["picker.home"] = "Home",
            ["picker.desktop"] = "Desktop",
            ["picker.downloads"] = "Downloads",
            ["picker.documents"] = "Documents",
            ["picker.cancel"] = "Cancel",
            ["picker.open"] = "Open",
            ["picker.folderUnreadable"] = "This folder cannot be read.",
            ["picker.fileMissing"] = "That file does not exist.",
            ["picker.wrongType"] = "That file type is not supported here.",

            // VRM versions
            ["vrm.0x"] = "VRM 0.x",
            ["vrm.10"] = "VRM 1.0",
            ["vrm.unknown"] = "Unknown",

            // Errors
            ["error.noFile"] = "No file was selected.",
            ["error.fileMissing"] = "The file could not be found. It may have been moved or deleted.",
            ["error.unreadable"] = "The file could not be read. Check that you have permission to open it.",
            ["error.notVrm"] = "This file is not a valid VRM file.",
            ["error.unsupportedGlb"] = "This file uses an unsupported glTF container version.",
            ["error.metadata"] = "VRM metadata could not be read.",
            ["error.noHumanoid"] = "The avatar has no compatible humanoid definition.",
            ["error.springBone"] = "The model loaded, but SpringBone data is invalid. Physics has been disabled.",
            ["error.importer"] = "The avatar could not be loaded. The file may be damaged or use features this version does not support.",
            ["error.cancelled"] = "Loading was cancelled.",
            ["error.versionMismatch"] = "This file was detected as {0}; loading it as {1} is not supported.",
            ["error.imageMissing"] = "The background image could not be found.",
            ["error.imageUnreadable"] = "The background image could not be read.",
            ["error.imageFormat"] = "The image format is not supported. Use PNG or JPEG.",
            ["error.imageLoad"] = "The image could not be loaded.",
        };
    }
}
