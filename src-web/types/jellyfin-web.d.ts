/**
 * Ambient types for the Jellyfin Web client globals that the injected
 * home-screen scripts run against. These are *not* part of @jellyfin/sdk
 * (which only covers the Server REST API). We keep them loose (`any`)
 * where jellyfin-web has no published types, and re-export SDK model
 * types for server payloads so the rest of the code can be strict.
 *
 * Re-export SDK types as type-only so they add zero runtime cost.
 */

// ---------------------------------------------------------------------------
// SDK model types (server API) — type-only, erased at build time
// ---------------------------------------------------------------------------
export type { BaseItemDto } from "@jellyfin/sdk/lib/generated-client/models/base-item-dto";
export type { QueryResult } from "@jellyfin/sdk/lib/generated-client/models/query-result-base-item-dto";

// ---------------------------------------------------------------------------
// Minimal surface of Jellyfin Web globals used by HomeScreenSections
// ---------------------------------------------------------------------------
declare global {
    interface Window {
        ApiClient: JellyfinApiClient;
        Dashboard: JellyfinDashboard;
        HssPageMeta: HssPageMeta;
        HssPageCache: HssPageCache;
        HssScrollHandler: (() => void) | undefined;
    }

    // jQuery is loaded globally by jellyfin-web — keep as `any` to avoid
    // pulling @types/jquery. The injected code only uses a tiny surface
    // ($.hasClass / .parents / .data / .on / .ready / .each).
    const $: any;

    // Module hooks replaced at runtime by TransformationPatches.cs
    // Keep as ambient consts so TS can parse the template before patching.
    // Build step (scripts/replace-placeholders.mjs) restores the {{...}} sentinels.
    const __LAYOUTMANAGER_HOOK__: { A: unknown };
    const __CARDBUILDER_HOOK__: { default: CardBuilder };
    const __THIS_HOOK__: unknown;
}

// ---------------------------------------------------------------------------
// Focused interfaces (expand as needed — keep `any` for untyped internals)
// ---------------------------------------------------------------------------
export interface JellyfinApiClient {
    getUrl(path: string, params?: Record<string, unknown>): string;
    getJSON<T = unknown>(url: string): Promise<T>;
    getCurrentUserId(): string;
    _currentUser: { Id: string };
    ajax(options: Record<string, unknown>): Promise<unknown>;
    serverId(): string;
}

export interface JellyfinDashboard {
    alert(message: string): void;
}

export interface HssPageMeta {
    Page: number | string;
    UsePagination: boolean;
    PaginationEnabled?: boolean;
    NumResultsPerPage?: number;
    ResultsPerPage?: number;
    LastScrollHeight: number;
    ScrollThreshold: number;
    PageHash: string;
    Finished?: boolean;
    IsLoading?: boolean;
    LastWindowHeight?: number;
    ScrollFixerHandle?: number | undefined;
}

export interface HssPageCache {
    elem: Element;
    apiClient: JellyfinApiClient;
    user: JellyfinUser;
    userSettings: JellyfinUserSettings;
}

export interface JellyfinUser {
    Id?: string;
    Policy?: { IsAdministrator?: boolean } | null;
}

export interface JellyfinUserSettings {
    maxDaysForNextUp(): number;
    enableRewatchingInNextUp(): boolean;
    useEpisodeImagesInNextUpAndResume(): boolean;
    getData?(): { CustomPrefs?: Record<string, string> } | null;
}

export interface CardBuilder {
    getCardsHtml(options: Record<string, unknown>): string;
}

// Section payloads returned by GET HomeScreen/Sections and GET HomeScreen/Section/{key}
export interface HomeScreenSectionInfo {
    Section: string;
    DisplayText: string;
    AdditionalData: string;
    OriginalPayload?: Record<string, unknown> | null;
    Route?: Record<string, unknown> | null;
    Limit: number;
    OrderIndex: number;
    ViewMode: "Portrait" | "Landscape" | "Square" | "Backdrop" | "Small";
    DisplayTitleText: boolean;
    ShowDetailsMenu: boolean;
    ContainerClass?: string;
}

export interface HomeScreenSectionsResponse {
    Items: HomeScreenSectionInfo[];
    TotalRecordCount: number;
}

export interface HomeScreenMetaResponse {
    Enabled?: boolean;
    AllowUserOverride?: boolean;
    PaginationEnabled?: boolean;
    NumResultsPerPage?: number;
}
