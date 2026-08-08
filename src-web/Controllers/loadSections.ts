import type { BaseItemDto } from "@jellyfin/sdk/lib/generated-client/models/base-item-dto";
import type {
    HomeScreenSectionInfo,
    HomeScreenSectionsResponse,
    HomeScreenMetaResponse,
    HssPageMeta,
    JellyfinApiClient,
    JellyfinUser,
    JellyfinUserSettings,
} from "../types/jellyfin-web";

// ---------------------------------------------------------------------------
// Placeholders — replaced at build time (scripts/replace-placeholders.mjs)
// and at runtime (TransformationPatches.cs). Keep as ambient consts so TS
// can parse the file before patching.
// ---------------------------------------------------------------------------
declare const __LAYOUTMANAGER_HOOK__: { A: unknown };
declare const __CARDBUILDER_HOOK__: { default: { getCardsHtml(options: Record<string, unknown>): string } };
declare const __THIS_HOOK__: {
    originalLoadSections(
        elem: Element | null,
        apiClient: JellyfinApiClient,
        user: JellyfinUser,
        userSettings: JellyfinUserSettings,
    ): unknown;
    loadSections(
        elem: Element | null,
        apiClient: JellyfinApiClient,
        user: JellyfinUser,
        userSettings: JellyfinUserSettings,
        page?: number | null,
    ): Promise<unknown>;
};

// Minified jellyfin-web chunk locals (stable per JF version — see TransformationPatches.cs)
declare const y: { UI: unknown; xK: unknown; zP: unknown };
declare const b: { Ay: unknown };
declare const p: { appRouter: { getRouteUrl(item: unknown, opts: unknown): string } };
declare const s: { Ay: { translate(key: string, ...args: string[]): string } };
declare const l: { default: { navigate(path: string): void } };
declare const u: { A: { getApiClient(serverId: string): JellyfinApiClient } };

// Local augmentation for ApiClient generics used in this file
type TypedApiClient = JellyfinApiClient & {
    getUrl(path: string, params?: Record<string, unknown>): string;
    getJSON<T>(url: string): Promise<T>;
    getCurrentUserId(): string;
    serverId(): string;
};

// ---------------------------------------------------------------------------
// Original file: Controllers/loadSections.js — now typed against @jellyfin/sdk.
// Only the two hook lines use the sentinels; everything else stays behaviour-
// identical so `TransformationPatchesTests` keeps passing.
// ---------------------------------------------------------------------------

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function test(
    this: typeof __THIS_HOOK__,
    elem: Element | null,
    apiClient: TypedApiClient,
    user: JellyfinUser,
    userSettings: JellyfinUserSettings,
    page: number | null = null,
): Promise<unknown> {
    function isHomePage(): boolean {
        const href: string = location.href || "";
        const hash: string = location.hash || "";

        const markers = {
            href,
            hash,
            indexPageId: document.getElementById("indexPage") !== null,
            homePageClass: document.querySelector(".homePage") !== null,
            pageHomePageClass: document.querySelector(".page.homePage") !== null,
            sectionsDiv: document.querySelector(".sections") !== null,
            pageRole: document.querySelector('[data-role="page"]') !== null,
            pageIdHome: document.querySelector('[data-pageid="home"]') !== null,
            routeHome: document.querySelector('[data-route="home"]') !== null,
        };

        const hrefL: string = href.toLowerCase();
        const hashL: string = hash.toLowerCase();

        const isHomeRoute: boolean =
            /(^#?!?\/?)(home)(\.html)?([/?&]|$)/.test(hashL) ||
            hashL.indexOf("home.html") !== -1 ||
            hrefL.indexOf("/web/index.html#!/home") !== -1;

        const isHomeDom: boolean =
            markers.sectionsDiv &&
            (markers.homePageClass || markers.pageHomePageClass || markers.indexPageId || markers.pageIdHome || markers.routeHome);

        return !!(isHomeRoute || isHomeDom);
    }

    if (!isHomePage()) {
        if (this && typeof this.originalLoadSections === "function") {
            return this.originalLoadSections(elem, apiClient, user, userSettings) as Promise<unknown>;
        }
        return Promise.resolve();
    }

    function getHomeScreenSectionFetchFn(
        _serverId: string,
        sectionInfo: HomeScreenSectionInfo,
        _serverConnections: typeof u.A,
        _userSettings: JellyfinUserSettings,
    ): () => Promise<QueryResultDto> {
        return function (): Promise<QueryResultDto> {
            const __userSettings: JellyfinUserSettings = _userSettings;

            const queryParams: Record<string, unknown> = {
                UserId: apiClient.getCurrentUserId(),
                AdditionalData: sectionInfo.AdditionalData,
                Language: localStorage.getItem(apiClient.getCurrentUserId() + "-language"),
            };

            if (sectionInfo.Section === "NextUp") {
                const cutoffDate = new Date();
                cutoffDate.setDate(cutoffDate.getDate() - __userSettings.maxDaysForNextUp());

                queryParams["NextUpDateCutoff"] = cutoffDate.toISOString();
                queryParams["EnableRewatching"] = __userSettings.enableRewatchingInNextUp();
            }

            const getUrl: string = apiClient.getUrl("HomeScreen/Section/" + sectionInfo.Section, queryParams);
            return apiClient.getJSON<QueryResultDto>(getUrl);
        };
    }

    type QueryResultDto = { Items?: BaseItemDto[]; TotalRecordCount?: number };

    function getHomeScreenSectionItemsHtmlFn(
        useEpisodeImages: boolean,
        enableOverflow: boolean,
        sectionKey: string,
        cardBuilder: { getCardsHtml(options: Record<string, unknown>): string },
        getShapeFn: (enableOverflow: boolean) => unknown,
        imageHelper: { getLibraryIcon(collectionType: string | undefined): string },
        appRouter: { getRouteUrl(item: unknown, opts?: unknown): string },
        additionalSettings: CardSettings,
    ): (items: BaseItemDto[]) => string {
        if (sectionKey === "DiscoverMovies" || sectionKey === "DiscoverTV" || sectionKey === "Discover") {
            return createDiscoverCards;
        }

        if (sectionKey.startsWith("Upcoming")) {
            return createUpcomingCards;
        }

        if (additionalSettings.ViewMode === "Small" && sectionKey === "MyMedia") {
            return function (items: BaseItemDto[]): string {
                let html = "";
                for (let i = 0; i < items.length; i++) {
                    const item: BaseItemDto = items[i] as BaseItemDto;
                    const icon: string = imageHelper.getLibraryIcon(
                        (item as unknown as { CollectionType?: string }).CollectionType,
                    );
                    html +=
                        '<a is="emby-linkbutton" href="' +
                        appRouter.getRouteUrl(item) +
                        '" class="raised homeLibraryButton"><span class="material-icons homeLibraryIcon ' +
                        icon +
                        '" aria-hidden="true"></span><span class="homeLibraryText">' +
                        (item.Name ?? "") +
                        "</span></a>";
                }
                return html;
            };
        }

        if (additionalSettings.ViewMode === "Small") {
            additionalSettings.ViewMode = "Landscape";
        }

        return function (items: BaseItemDto[]): string {
            return cardBuilder.getCardsHtml({
                items: items,
                preferThumb: additionalSettings.ViewMode === "Portrait" ? null : "auto",
                inheritThumb: !useEpisodeImages,
                shape: getShapeFn(enableOverflow),
                overlayText: false,
                showTitle: additionalSettings.DisplayTitleText,
                showParentTitle: additionalSettings.DisplayTitleText,
                lazy: true,
                showDetailsMenu: additionalSettings.ShowDetailsMenu,
                overlayPlayButton: "MyMedia" !== sectionKey,
                context: "home",
                centerText: true,
                allowBottomPadding: false,
                cardLayout: false,
                showYear: true,
                lines: additionalSettings.DisplayTitleText ? (sectionKey === "MyMedia" ? 1 : 2) : 0,
            });
        };
    }

    interface CardSettings {
        ViewMode: HomeScreenSectionInfo["ViewMode"];
        DisplayTitleText: boolean;
        ShowDetailsMenu: boolean;
    }

    function createDiscoverCards(items: BaseItemDto[]): string {
        let html = "";
        let index = 0;
        items.forEach((item: BaseItemDto): void => {
            const providerIds = (item as unknown as { ProviderIds?: Record<string, string> }).ProviderIds ?? {};
            html +=
                '<div tabindex="0" class="card overflowPortraitCard card-hoverable card-withuserdata discover-card" data-index="' +
                index +
                '" data-tmdb-id="' +
                (providerIds["Jellyseerr"] ?? "") +
                '" data-media-type="' +
                (providerIds["Jellyseerr"] ? (item as unknown as { SourceType?: string }).SourceType ?? "" : "") +
                '">';
            html += '   <div class="cardBox cardBox-bottompadded">';
            html += '       <div class="cardScalable discoverCard-' + ((item as unknown as { SourceType?: string }).SourceType ?? "") + '">';
            html += '           <div class="cardPadder cardPadder-overflowPortrait lazy-hidden-children"></div>';
            html += '           <canvas aria-hidden="true" width="20" height="20" class="blurhash-canvas lazy-hidden"></canvas>';

            let posterUrl: string = providerIds["JellyseerrPoster"] ?? "";
            if (posterUrl && !posterUrl.startsWith("http")) {
                posterUrl = window.ApiClient.getUrl(posterUrl);
            }

            html +=
                '           <a is="emby-linkbutton" target="_blank" href="' +
                (providerIds["JellyseerrRoot"] ?? "") +
                "/" +
                ((item as unknown as { SourceType?: string }).SourceType ?? "") +
                "/" +
                (providerIds["Jellyseerr"] ?? "") +
                '" class="cardImageContainer coveredImage cardContent itemAction lazy blurhashed lazy-image-fadein-fast" aria-label="" style="background-image: url(\'' +
                posterUrl +
                "');color: inherit; text-decoration: none;\"></a>";
            html += '           <div class="cardOverlayContainer itemAction" data-action="link">';
            html +=
                '               <a is="emby-linkbutton" target="_blank" href="' +
                (providerIds["JellyseerrRoot"] ?? "") +
                "/" +
                ((item as unknown as { SourceType?: string }).SourceType ?? "") +
                "/" +
                (providerIds["Jellyseerr"] ?? "") +
                '" class="cardImageContainer"  style="color: inherit; text-decoration: none;"></a>';
            html += '               <div class="cardOverlayButton-br flex">';
            html +=
                '                   <button is="discover-requestbutton" type="button" data-action="none" class="discover-requestbutton cardOverlayButton cardOverlayButton-hover itemAction paper-icon-button-light emby-button" data-id="' +
                (providerIds["Jellyseerr"] ?? "") +
                '" data-media-type="' +
                ((item as unknown as { SourceType?: string }).SourceType ?? "") +
                '">';
            html += '                       <span class="material-icons cardOverlayButtonIcon cardOverlayButtonIcon-hover add" aria-hidden="true"></span>';
            html += "                   </button>";
            html += "               </div>";
            html += "           </div>";
            html += "       </div>";
            html += '       <div class="cardText cardTextCentered cardText-first">';
            html += "           <bdi>";
            html +=
                '               <a is="emby-linkbutton" style="color: inherit; text-decoration: none;" target="_blank" href="' +
                (providerIds["JellyseerrRoot"] ?? "") +
                "/" +
                ((item as unknown as { SourceType?: string }).SourceType ?? "") +
                "/" +
                (providerIds["Jellyseerr"] ?? "") +
                '" class="itemAction textActionButton" title="' +
                (item.Name ?? "") +
                '" data-action="link">' +
                (item.Name ?? "") +
                "</a>";
            html += "           </bdi>";
            html += "       </div>";
            html += '       <div class="cardText cardTextCentered cardText-secondary">';
            html += "           <bdi>";

            const date = new Date((item as unknown as { PremiereDate?: string }).PremiereDate ?? "");
            let yearText = "";
            const communityRating: number | undefined = (item as unknown as { CommunityRating?: number }).CommunityRating;
            if (communityRating) {
                const rating: string = communityRating.toFixed(1);
                yearText +=
                    '<span class="material-icons" style="font-size: 14px; vertical-align: middle; color: #FFD700;">star</span> ' + rating + " • ";
            } else {
                yearText += '<span class="material-icons" style="font-size: 14px; vertical-align: middle; color: #FFD700;">star</span> - • ';
            }
            yearText += date.getFullYear();
            html +=
                '               <a is="emby-linkbutton" style="color: inherit; text-decoration: none;" target="_blank" href="' +
                (providerIds["JellyseerrRoot"] ?? "") +
                "/" +
                ((item as unknown as { SourceType?: string }).SourceType ?? "") +
                "/" +
                (providerIds["Jellyseerr"] ?? "") +
                '" class="itemAction textActionButton" title="' +
                String(date.getFullYear()) +
                '" data-action="link">' +
                yearText +
                "</a>";
            html += "           </bdi>";
            html += "       </div>";
            html += "   </div>";
            html += "</div>";
            index++;
        });

        return html;
    }

    function createUpcomingCards(items: BaseItemDto[]): string {
        let html = "";
        let index = 0;
        items.forEach((item: BaseItemDto): void => {
            const providerIds = (item as unknown as { ProviderIds?: Record<string, string> }).ProviderIds ?? {};
            const formattedDate: string = providerIds["FormattedDate"] || "";

            let contentType: string;
            let title: string;
            let secondaryInfo: string | undefined;
            let posterUrl: string;
            let cardClass: string;
            let cardScalableClass: string;
            let cardShapeClass = "overflowPortraitCard";
            let cardPadderClass = "cardPadder-overflowPortrait";

            const itemType: string | undefined = (item as unknown as { Type?: string }).Type;
            if (itemType === "Episode" || providerIds["SonarrSeriesId"]) {
                contentType = "show";
                title = (item as unknown as { SeriesName?: string }).SeriesName || (item.Name ?? "Unknown Series");
                secondaryInfo = providerIds["EpisodeInfo"] || "";
                posterUrl = providerIds["SonarrPoster"] || "";
                cardClass = "upcoming-show-card";
                cardScalableClass = "upcomingShowCard";
            } else if (itemType === "Movie" || providerIds["RadarrMovieId"]) {
                contentType = "movie";
                title = item.Name || "Unknown Movie";
                posterUrl = providerIds["RadarrPoster"] || "";
                cardClass = "upcoming-movie-card";
                cardScalableClass = "upcomingMovieCard";
            } else if (itemType === "MusicAlbum" || providerIds["LidarrArtistId"]) {
                contentType = "music";
                title = item.Name || "Unknown Album";
                secondaryInfo = (item as unknown as { Overview?: string }).Overview || "";
                posterUrl = providerIds["LidarrPoster"] || "";
                cardClass = "upcoming-music-card";
                cardScalableClass = "upcomingMusicCard";
                cardShapeClass = "overflowSquareCard";
                cardPadderClass = "cardPadder-square";
            } else if (itemType === "Book" || providerIds["ReadarrBookId"]) {
                contentType = "book";
                title = item.Name || "Unknown Book";
                secondaryInfo = (item as unknown as { Overview?: string }).Overview || "";
                posterUrl = providerIds["ReadarrPoster"] || "";
                cardClass = "upcoming-book-card";
                cardScalableClass = "upcomingBookCard";
            } else {
                contentType = "unknown";
                title = item.Name || "Unknown";
                posterUrl = "";
                cardClass = "upcoming-unknown-card";
                cardScalableClass = "upcomingUnknownCard";
            }

            html +=
                '<div tabindex="0" class="card ' +
                cardShapeClass +
                ' card-hoverable card-withuserdata ' +
                cardClass +
                '" data-index="' +
                index +
                '" data-content-type="' +
                contentType +
                '">';
            html += '   <div class="cardBox cardBox-bottompadded">';
            html += '       <div class="cardScalable ' + cardScalableClass + '">';
            html += '           <div class="cardPadder ' + cardPadderClass + ' lazy-hidden-children"></div>';

            if (posterUrl) {
                if (!posterUrl.startsWith("http")) {
                    posterUrl = window.ApiClient.getUrl(posterUrl);
                }
                html += '           <div class="cardImageContainer coveredImage cardContent lazy blurhashed lazy-image-fadein-fast" style="background-image: url(\'' + posterUrl + "')\"></div>";
            } else {
                html += '           <canvas aria-hidden="true" width="20" height="20" class="blurhash-canvas lazy-hidden"></canvas>';
            }

            html += "       </div>";
            html += '       <div class="cardText cardTextCentered cardText-first">';
            html += "           <bdi>";
            html += '               <div class="itemAction textActionButton" title="' + title + '">' + title + "</div>";
            html += "           </bdi>";
            html += "       </div>";

            if (secondaryInfo) {
                html += '       <div class="cardText cardTextCentered cardText-secondary">';
                html += "           <bdi>";
                html += '               <div class="itemAction textActionButton" title="' + secondaryInfo + '">' + secondaryInfo + "</div>";
                html += "           </bdi>";
                html += "       </div>";
            }

            if (formattedDate) {
                html += '       <div class="cardText cardTextCentered cardText-tertiary">';
                html += "           <bdi>";
                html += '               <div class="itemAction textActionButton" title="' + formattedDate + '">' + formattedDate + "</div>";
                html += "           </bdi>";
                html += "       </div>";
            }

            html += "   </div>";
            html += "</div>";
            index++;
        });

        return html;
    }

    function getSectionClass(sectionInfo: HomeScreenSectionInfo): string {
        if (sectionInfo.Limit > 1) {
            return sectionInfo.Section + "-" + sectionInfo.AdditionalData.replace(" ", "-").replace(".", "-").replace("'", "");
        } else {
            return sectionInfo.Section;
        }
    }

    function loadHomeSection(
        pageEl: Element,
        _apiClient: TypedApiClient,
        _user: JellyfinUser,
        userSettings: JellyfinUserSettings,
        sectionInfo: HomeScreenSectionInfo,
        options: { sectionIndex?: number; enableOverflow?: boolean },
    ): Promise<void> {
        const sectionClass: string = getSectionClass(sectionInfo);
        console.log("Loading section: ." + sectionClass + ", could also be .section" + options.sectionIndex);

        const elem: Element | null = pageEl.querySelector("." + sectionClass + '[data-page="' + (window.HssPageMeta.Page as string) + '"]');
        if (null !== elem) {
            let html = "";
            // Sentinel — replaced to {{layoutmanager_hook}}.A by scripts/replace-placeholders.mjs
            const layoutManager: { tv: boolean } = (__LAYOUTMANAGER_HOOK__ as unknown as { A: { tv: boolean } }).A;
            html += '<div class="sectionTitleContainer sectionTitleContainer-cards padded-left">';

            let titleRoute: string | undefined = undefined;
            if (!layoutManager.tv) {
                try {
                    if (sectionInfo.OriginalPayload) {
                        titleRoute = p.appRouter.getRouteUrl(sectionInfo.OriginalPayload, {
                            serverId: _apiClient.serverId(),
                        });
                    }
                    if ((!titleRoute || titleRoute === "#" || titleRoute === "undefined") && sectionInfo.Route) {
                        titleRoute = p.appRouter.getRouteUrl(sectionInfo.Route, {
                            serverId: _apiClient.serverId(),
                        });
                    }
                } catch (titleLinkError: unknown) {
                    console.warn("Home Screen Sections: failed to resolve title route for", sectionInfo.Section, titleLinkError);
                    titleRoute = undefined;
                }
            }

            const hasTitleLink: boolean = !!(
                titleRoute &&
                titleRoute !== "#" &&
                String(titleRoute).indexOf("undefined") === -1 &&
                String(titleRoute).length > 1
            );
            if (hasTitleLink) {
                html +=
                    '<a is="emby-linkbutton" href="' +
                    titleRoute +
                    '" class="button-flat button-flat-mini sectionTitleTextButton" title="' +
                    (sectionInfo.DisplayText || "") +
                    '">';
                html += '<h2 class="sectionTitle sectionTitle-cards">';
                html += sectionInfo.DisplayText;
                html += "</h2>";
                html += '<span class="material-icons chevron_right" aria-hidden="true"></span>';
                html += "</a>";
            } else {
                html += '<h2 class="sectionTitle sectionTitle-cards">';
                html += sectionInfo.DisplayText;
                html += "</h2>";
            }

            html += "</div>";

            if (sectionInfo.ViewMode !== "Small") {
                html += '<div is="emby-scroller" class="padded-top-focusscale padded-bottom-focusscale" data-centerfocus="true">';
                html +=
                    '<div is="emby-itemscontainer" class="itemsContainer scrollSlider focuscontainer-x animatedScrollX" data-monitor="videoplayback,markplayed">';
            } else {
                html +=
                    '<div is="emby-itemscontainer" class="itemsContainer padded-left padded-right vertical-wrap focuscontainer-x" data-monitor="videoplayback,markplayed">';
            }

            html += "</div>";

            if (sectionInfo.ViewMode !== "Small") {
                html += "</div>";
            }
            elem.classList.add("hide");
            elem.innerHTML = html;

            const itemsContainer: HTMLElement | null = elem.querySelector(".itemsContainer") as HTMLElement | null;

            if (itemsContainer !== null) {
                if (sectionInfo.ContainerClass !== undefined) {
                    itemsContainer.classList.add(sectionInfo.ContainerClass);
                }

                // Sentinel — replaced to {{cardbuilder_hook}}.default
                const cardBuilder: { getCardsHtml(options: Record<string, unknown>): string } = (
                    __CARDBUILDER_HOOK__ as unknown as { default: { getCardsHtml(options: Record<string, unknown>): string } }
                ).default;

                const cardSettings: CardSettings = {
                    ViewMode: sectionInfo.ViewMode,
                    DisplayTitleText: sectionInfo.DisplayTitleText,
                    ShowDetailsMenu: sectionInfo.ShowDetailsMenu,
                };

                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                (itemsContainer as any).fetchData = getHomeScreenSectionFetchFn(_apiClient.serverId(), sectionInfo, u.A, userSettings);

                const getBackdropShape: (enableOverflow: boolean) => unknown = y.UI as (enableOverflow: boolean) => unknown;
                const getPortraitShape: (enableOverflow: boolean) => unknown = y.xK as (enableOverflow: boolean) => unknown;
                const getSquareShape: (enableOverflow: boolean) => unknown = y.zP as (enableOverflow: boolean) => unknown;

                let getShapeFn: (enableOverflow: boolean) => unknown = getBackdropShape;
                if (cardSettings.ViewMode === "Portrait") {
                    getShapeFn = getPortraitShape;
                } else if (cardSettings.ViewMode === "Square") {
                    getShapeFn = getSquareShape;
                } else if (cardSettings.ViewMode === "Backdrop") {
                    getShapeFn = getBackdropShape;
                }

                const imageHelper: { getLibraryIcon(collectionType: string | undefined): string } = b.Ay as {
                    getLibraryIcon(collectionType: string | undefined): string;
                };

                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                (itemsContainer as any).getItemsHtml = getHomeScreenSectionItemsHtmlFn(
                    userSettings.useEpisodeImagesInNextUpAndResume(),
                    options.enableOverflow ?? true,
                    sectionInfo.Section,
                    cardBuilder,
                    getShapeFn,
                    imageHelper,
                    p.appRouter,
                    cardSettings,
                );
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                (itemsContainer as any).parentContainer = elem;
            }
        }
        return Promise.resolve();
    }

    function getHomeScreenSectionsMeta(_apiClient: TypedApiClient): Promise<HomeScreenMetaResponse> {
        return _apiClient.getJSON<HomeScreenMetaResponse>(_apiClient.getUrl("HomeScreen/Meta"));
    }

    function isUserUsingHomeScreenSections(pluginMeta: HomeScreenMetaResponse, _userSettings: JellyfinUserSettings): boolean {
        try {
            if (pluginMeta && pluginMeta.AllowUserOverride === true) {
                const data: { CustomPrefs?: Record<string, string> } | null | undefined =
                    _userSettings && _userSettings.getData ? _userSettings.getData() : null;
                if (data && data.CustomPrefs && data.CustomPrefs["useModularHome"] !== undefined) {
                    return data.CustomPrefs["useModularHome"] === "true";
                }
            }
            return !!(pluginMeta && pluginMeta.Enabled);
        } catch (_e: unknown) {
            return false;
        }
    }

    function uuidv4(): string {
        return "10000000-1000-4000-8000-100000000000".replace(/[018]/g, (c: string): string =>
            (+c ^ (crypto.getRandomValues(new Uint8Array(1))[0] & (15 >> (+c / 4)))).toString(16),
        );
    }

    const _this: typeof __THIS_HOOK__ = this;

    return getHomeScreenSectionsMeta(apiClient)
        .then((hssMeta: HomeScreenMetaResponse): Promise<unknown> => {
            const useHss: boolean = isUserUsingHomeScreenSections(hssMeta, userSettings);

            if (!useHss) {
                return _this.originalLoadSections(elem, apiClient, user, userSettings) as Promise<unknown>;
            }

            if (page !== null) {
                (window.HssPageMeta as unknown as Record<string, unknown>)["Page"] = page;
            } else {
                window.HssPageMeta = {
                    UsePagination: hssMeta.PaginationEnabled ?? false,
                    Page: 1,
                    ResultsPerPage: hssMeta.NumResultsPerPage ?? 20,
                    LastScrollHeight: 0,
                    ScrollThreshold: 10,
                    PageHash: uuidv4(),
                } as unknown as HssPageMeta;

                window.HssPageCache = {
                    elem: elem as Element,
                    apiClient: apiClient,
                    user: user,
                    userSettings: userSettings,
                };

                if (typeof window.HssScrollHandler === "function") {
                    window.removeEventListener("scroll", window.HssScrollHandler);
                }

                window.HssScrollHandler = function (): void {
                    const scrollPosition: number = window.scrollY + window.innerHeight;
                    const windowHeight: number = getDocHeight();

                    if (
                        window.HssPageMeta.Finished !== true &&
                        window.HssPageMeta.IsLoading !== true &&
                        scrollPosition > windowHeight - window.HssPageMeta.ScrollThreshold &&
                        (window.HssPageMeta.LastScrollHeight as number) < window.scrollY
                    ) {
                        (window.HssPageMeta as unknown as Record<string, boolean>)["IsLoading"] = true;

                        const indicator: HTMLElement | null = document.querySelector("#hssLoadingIndicator") as HTMLElement | null;
                        if (indicator) {
                            indicator.style.display = "block";
                        }

                        let winHeight: number = getDocHeight();
                        window.scroll(0, winHeight - (window.innerHeight + window.HssPageMeta.ScrollThreshold));

                        (window.HssPageMeta as unknown as Record<string, number>)["LastScrollHeight"] = window.scrollY;
                        (window.HssPageMeta as unknown as Record<string, number>)["LastWindowHeight"] = winHeight;

                        (window.HssPageMeta as unknown as Record<string, number | undefined>)["ScrollFixerHandle"] = window.setInterval(function (): void {
                            window.scroll(0, window.HssPageMeta.LastScrollHeight as number);

                            if (getDocHeight() > (window.HssPageMeta.LastWindowHeight as number)) {
                                clearInterval(window.HssPageMeta.ScrollFixerHandle as number);
                                window.HssPageMeta.ScrollFixerHandle = undefined;
                            }
                        }, 1);

                        void _this
                            .loadSections(
                                window.HssPageCache.elem,
                                window.HssPageCache.apiClient,
                                window.HssPageCache.user,
                                window.HssPageCache.userSettings,
                                (window.HssPageMeta.Page as number) + 1,
                            )
                            .then(function (): void {
                                const ind: HTMLElement | null = document.querySelector("#hssLoadingIndicator") as HTMLElement | null;
                                if (ind) {
                                    ind.style.display = "none";
                                }

                                (window.HssPageMeta as unknown as Record<string, boolean>)["IsLoading"] = false;

                                if (window.HssPageMeta.ScrollFixerHandle) {
                                    clearInterval(window.HssPageMeta.ScrollFixerHandle as number);
                                }
                            });
                    }

                    function getDocHeight(): number {
                        const D: Document = document;
                        return Math.max(
                            D.body.scrollHeight,
                            D.documentElement.scrollHeight,
                            D.body.offsetHeight,
                            D.documentElement.offsetHeight,
                            D.body.clientHeight,
                            D.documentElement.clientHeight,
                        );
                    }
                };

                window.addEventListener("scroll", window.HssScrollHandler);
            }

            const getSectionsData: Record<string, unknown> = {
                UserId: apiClient.getCurrentUserId(),
                Language: localStorage.getItem(apiClient.getCurrentUserId() + "-language"),
            };

            if (window.HssPageMeta.UsePagination) {
                getSectionsData["Page"] = window.HssPageMeta.Page;
                getSectionsData["NumResultsPerPage"] = window.HssPageMeta.ResultsPerPage;
                getSectionsData["PageHash"] = window.HssPageMeta.PageHash;
            } else {
                (window.HssPageMeta as unknown as Record<string, boolean>)["Finished"] = true;
            }

            const getSectionsUrl: string = apiClient.getUrl("HomeScreen/Sections", getSectionsData);

            return apiClient.getJSON<HomeScreenSectionsResponse>(getSectionsUrl).then(
                function (response: HomeScreenSectionsResponse): unknown {
                    if (response.TotalRecordCount === 0 && (window.HssPageMeta.Page as number) > 1) {
                        window.HssPageMeta.Finished = true;
                        return function (
                            _elem: unknown,
                            _apiClient: unknown,
                            _user: unknown,
                            _userSettings: unknown,
                        ): void {};
                    }
                    return function (
                        this: unknown,
                        elemArg: Element,
                        apiClientArg: TypedApiClient,
                        userArg: JellyfinUser,
                        userSettingsArg: JellyfinUserSettings,
                    ): Promise<unknown> {
                        // Keep the original obfuscated generator shape behaviourally identical
                        // but expressed with async/await for type safety — the outer contract
                        // (returns Promise that resolves after sections are rendered) is preserved.
                        return (async (): Promise<unknown> => {
                            const var44: HomeScreenSectionsResponse = response;
                            const options: { enableOverflow: boolean; sectionIndex?: number } = {
                                enableOverflow: true,
                            };
                            let var44_3 = "";
                            const var44_4: Promise<void>[] = [];
                            if (void 0 !== var44.Items) {
                                const existingContainer: Element | null = document.querySelector(".homeSectionsContainer");
                                let existingSections = 0;
                                if (existingContainer !== null) {
                                    existingSections = existingContainer.children.length;
                                }
                                for (let var44_5 = 0; var44_5 < var44.TotalRecordCount; var44_5++) {
                                    const var44_6: string = getSectionClass(var44.Items[var44_5]);
                                    void var44.Items[var44_5].Limit;
                                    var44_3 +=
                                        '<div data-page="' +
                                        String(window.HssPageMeta.Page) +
                                        '" style="order:' +
                                        String(
                                            var44.Items[var44_5].OrderIndex + 1000 * ((window.HssPageMeta.Page as number) - 1),
                                        ) +
                                        ';" class="verticalSection ' +
                                        var44_6 +
                                        " section" +
                                        String(existingSections + var44_5) +
                                        '"></div>';
                                }

                                if ((window.HssPageMeta.Page as number) !== 1) {
                                    const tempContainer: HTMLDivElement = document.createElement("div");
                                    tempContainer.innerHTML = var44_3;

                                    while (tempContainer.firstChild) {
                                        elemArg.appendChild(tempContainer.firstChild);
                                    }
                                } else {
                                    const spinnerHtml: string =
                                        '<div id="hssLoadingIndicator" class="verticalSection" style="order: 2147000000;margin-top:60px;margin-bottom:60px;display:none;"><div dir="ltr" class="docspinner mdl-spinner mdlSpinnerActive" style="position: relative;top: 0;left: calc(50vw - 1.5em);"><div class="mdl-spinner__layer mdl-spinner__layer-1"><div class="mdl-spinner__circle-clipper mdl-spinner__left"><div class="mdl-spinner__circle mdl-spinner__circleLeft"></div></div><div class="mdl-spinner__circle-clipper mdl-spinner__right"><div class="mdl-spinner__circle mdl-spinner__circleRight"></div></div></div><div class="mdl-spinner__layer mdl-spinner__layer-2"><div class="mdl-spinner__circle-clipper mdl-spinner__left"><div class="mdl-spinner__circle mdl-spinner__circleLeft"></div></div><div class="mdl-spinner__circle-clipper mdl-spinner__right"><div class="mdl-spinner__circle mdl-spinner__circleRight"></div></div></div><div class="mdl-spinner__layer mdl-spinner__layer-3"><div class="mdl-spinner__circle-clipper mdl-spinner__left"><div class="mdl-spinner__circle mdl-spinner__circleLeft"></div></div><div class="mdl-spinner__circle-clipper mdl-spinner__right"><div class="mdl-spinner__circle mdl-spinner__circleRight"></div></div></div><div class="mdl-spinner__layer mdl-spinner__layer-4"><div class="mdl-spinner__circle-clipper mdl-spinner__left"><div class="mdl-spinner__circle mdl-spinner__circleLeft"></div></div><div class="mdl-spinner__circle-clipper mdl-spinner__right"><div class="mdl-spinner__circle mdl-spinner__circleRight"></div></div></div></div></div>';

                                    elemArg.innerHTML = spinnerHtml + var44_3;
                                }

                                if (!elemArg.classList.contains("homeSectionsContainer")) {
                                    elemArg.classList.add("homeSectionsContainer");
                                }

                                if (var44.TotalRecordCount > 0) {
                                    for (let var44_7 = 0; var44_7 < var44.Items.length; var44_7++) {
                                        const sectionInfo: HomeScreenSectionInfo = var44.Items[var44_7];
                                        options.sectionIndex = var44_7;
                                        var44_4.push(loadHomeSection(elemArg, apiClientArg, 0 as unknown as JellyfinUser, userSettingsArg, sectionInfo, options));
                                    }
                                }
                            }
                            return var44.TotalRecordCount > 0
                                ? Promise.all(var44_4).then(function (): Promise<unknown[]> {
                                      const var134_2: { refresh: boolean } = { refresh: true };
                                      const var134_3: NodeListOf<Element> = elemArg.querySelectorAll(
                                          '[data-page="' + String(window.HssPageMeta.Page) + '"] .itemsContainer',
                                      );
                                      const var134_4: Promise<unknown>[] = [];
                                      Array.prototype.forEach.call(var134_3, function (param139: HTMLElement & { resume?: (o: unknown) => Promise<unknown> }): void {
                                          if (param139.resume) {
                                              var134_4.push(param139.resume(var134_2));
                                          }
                                      });
                                      return Promise.all(var134_4);
                                  })
                                : ((): unknown => {
                                      const isAdmin: boolean | undefined = (userArg.Policy as { IsAdministrator?: boolean } | null | undefined)?.IsAdministrator;
                                      const var44_9: string = isAdmin
                                          ? s.Ay.translate("NoCreatedLibraries", '<br><a id="button-createLibrary" class="button-link">', "</a>")
                                          : s.Ay.translate("AskAdminToCreateLibrary");
                                      var44_3 += '<div class="centerMessage padded-left padded-right">';
                                      var44_3 += "<h2>" + s.Ay.translate("MessageNothingHere") + "</h2>";
                                      var44_3 += "<p>" + var44_9 + "</p>";
                                      var44_3 += "</div>";
                                      elemArg.innerHTML = var44_3;
                                      const var44_10: HTMLElement | null = elemArg.querySelector("#button-createLibrary") as HTMLElement | null;
                                      if (var44_10) {
                                          var44_10.addEventListener("click", function (): void {
                                              l.default.navigate("dashboard/libraries");
                                          });
                                      }
                                      return undefined;
                                  })();
                        })();
                    } as unknown as unknown;
                },
                function (error: unknown): Promise<unknown> {
                    console.error("Error fetching sections with HSS, defaulting back to Jellyfin:", error);
                    return _this.originalLoadSections(elem, apiClient, user, userSettings) as Promise<unknown>;
                },
            );
        },
        function (_err: unknown): Promise<unknown> {
            return _this.originalLoadSections(elem, apiClient, user, userSettings) as Promise<unknown>;
        },
    );
}
