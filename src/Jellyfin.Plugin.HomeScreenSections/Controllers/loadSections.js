// ---------------------------------------------------------------------------
// Original file: Controllers/loadSections.js — now typed against @jellyfin/sdk.
// Only the two hook lines use the sentinels; everything else stays behaviour-
// identical so `TransformationPatchesTests` keeps passing.
// ---------------------------------------------------------------------------
// eslint-disable-next-line @typescript-eslint/no-explicit-any
function test(elem, apiClient, user, userSettings, page = null) {
    function isHomePage() {
        const href = location.href || "";
        const hash = location.hash || "";
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
        const hrefL = href.toLowerCase();
        const hashL = hash.toLowerCase();
        const isHomeRoute = /(^#?!?\/?)(home)(\.html)?([/?&]|$)/.test(hashL) ||
            hashL.indexOf("home.html") !== -1 ||
            hrefL.indexOf("/web/index.html#!/home") !== -1;
        const isHomeDom = markers.sectionsDiv &&
            (markers.homePageClass || markers.pageHomePageClass || markers.indexPageId || markers.pageIdHome || markers.routeHome);
        return !!(isHomeRoute || isHomeDom);
    }
    if (!isHomePage()) {
        if (this && typeof this.originalLoadSections === "function") {
            return this.originalLoadSections(elem, apiClient, user, userSettings);
        }
        return Promise.resolve();
    }
    function getHomeScreenSectionFetchFn(_serverId, sectionInfo, _serverConnections, _userSettings) {
        return function () {
            const __userSettings = _userSettings;
            const queryParams = {
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
            const getUrl = apiClient.getUrl("HomeScreen/Section/" + sectionInfo.Section, queryParams);
            return apiClient.getJSON(getUrl);
        };
    }
    function getHomeScreenSectionItemsHtmlFn(useEpisodeImages, enableOverflow, sectionKey, cardBuilder, getShapeFn, imageHelper, appRouter, additionalSettings) {
        if (sectionKey === "DiscoverMovies" || sectionKey === "DiscoverTV" || sectionKey === "Discover") {
            return createDiscoverCards;
        }
        if (sectionKey.startsWith("Upcoming")) {
            return createUpcomingCards;
        }
        if (additionalSettings.ViewMode === "Small" && sectionKey === "MyMedia") {
            return function (items) {
                var _a;
                let html = "";
                for (let i = 0; i < items.length; i++) {
                    const item = items[i];
                    const icon = imageHelper.getLibraryIcon(item.CollectionType);
                    html +=
                        '<a is="emby-linkbutton" href="' +
                            appRouter.getRouteUrl(item) +
                            '" class="raised homeLibraryButton"><span class="material-icons homeLibraryIcon ' +
                            icon +
                            '" aria-hidden="true"></span><span class="homeLibraryText">' +
                            ((_a = item.Name) !== null && _a !== void 0 ? _a : "") +
                            "</span></a>";
                }
                return html;
            };
        }
        if (additionalSettings.ViewMode === "Small") {
            additionalSettings.ViewMode = "Landscape";
        }
        return function (items) {
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
    function createDiscoverCards(items) {
        let html = "";
        let index = 0;
        items.forEach((item) => {
            var _a, _b, _c, _d, _f, _g, _h, _j, _k, _l, _m, _o, _p, _q, _r, _s, _t, _u, _v, _w, _x, _y;
            const providerIds = (_a = item.ProviderIds) !== null && _a !== void 0 ? _a : {};
            html +=
                '<div tabindex="0" class="card overflowPortraitCard card-hoverable card-withuserdata discover-card" data-index="' +
                    index +
                    '" data-tmdb-id="' +
                    ((_b = providerIds["Jellyseerr"]) !== null && _b !== void 0 ? _b : "") +
                    '" data-media-type="' +
                    (providerIds["Jellyseerr"] ? (_c = item.SourceType) !== null && _c !== void 0 ? _c : "" : "") +
                    '">';
            html += '   <div class="cardBox cardBox-bottompadded">';
            html += '       <div class="cardScalable discoverCard-' + ((_d = item.SourceType) !== null && _d !== void 0 ? _d : "") + '">';
            html += '           <div class="cardPadder cardPadder-overflowPortrait lazy-hidden-children"></div>';
            html += '           <canvas aria-hidden="true" width="20" height="20" class="blurhash-canvas lazy-hidden"></canvas>';
            let posterUrl = (_f = providerIds["JellyseerrPoster"]) !== null && _f !== void 0 ? _f : "";
            if (posterUrl && !posterUrl.startsWith("http")) {
                posterUrl = window.ApiClient.getUrl(posterUrl);
            }
            html +=
                '           <a is="emby-linkbutton" target="_blank" href="' +
                    ((_g = providerIds["JellyseerrRoot"]) !== null && _g !== void 0 ? _g : "") +
                    "/" +
                    ((_h = item.SourceType) !== null && _h !== void 0 ? _h : "") +
                    "/" +
                    ((_j = providerIds["Jellyseerr"]) !== null && _j !== void 0 ? _j : "") +
                    '" class="cardImageContainer coveredImage cardContent itemAction lazy blurhashed lazy-image-fadein-fast" aria-label="" style="background-image: url(\'' +
                    posterUrl +
                    "');color: inherit; text-decoration: none;\"></a>";
            html += '           <div class="cardOverlayContainer itemAction" data-action="link">';
            html +=
                '               <a is="emby-linkbutton" target="_blank" href="' +
                    ((_k = providerIds["JellyseerrRoot"]) !== null && _k !== void 0 ? _k : "") +
                    "/" +
                    ((_l = item.SourceType) !== null && _l !== void 0 ? _l : "") +
                    "/" +
                    ((_m = providerIds["Jellyseerr"]) !== null && _m !== void 0 ? _m : "") +
                    '" class="cardImageContainer"  style="color: inherit; text-decoration: none;"></a>';
            html += '               <div class="cardOverlayButton-br flex">';
            html +=
                '                   <button is="discover-requestbutton" type="button" data-action="none" class="discover-requestbutton cardOverlayButton cardOverlayButton-hover itemAction paper-icon-button-light emby-button" data-id="' +
                    ((_o = providerIds["Jellyseerr"]) !== null && _o !== void 0 ? _o : "") +
                    '" data-media-type="' +
                    ((_p = item.SourceType) !== null && _p !== void 0 ? _p : "") +
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
                    ((_q = providerIds["JellyseerrRoot"]) !== null && _q !== void 0 ? _q : "") +
                    "/" +
                    ((_r = item.SourceType) !== null && _r !== void 0 ? _r : "") +
                    "/" +
                    ((_s = providerIds["Jellyseerr"]) !== null && _s !== void 0 ? _s : "") +
                    '" class="itemAction textActionButton" title="' +
                    ((_t = item.Name) !== null && _t !== void 0 ? _t : "") +
                    '" data-action="link">' +
                    ((_u = item.Name) !== null && _u !== void 0 ? _u : "") +
                    "</a>";
            html += "           </bdi>";
            html += "       </div>";
            html += '       <div class="cardText cardTextCentered cardText-secondary">';
            html += "           <bdi>";
            const date = new Date((_v = item.PremiereDate) !== null && _v !== void 0 ? _v : "");
            let yearText = "";
            const communityRating = item.CommunityRating;
            if (communityRating) {
                const rating = communityRating.toFixed(1);
                yearText +=
                    '<span class="material-icons" style="font-size: 14px; vertical-align: middle; color: #FFD700;">star</span> ' + rating + " • ";
            }
            else {
                yearText += '<span class="material-icons" style="font-size: 14px; vertical-align: middle; color: #FFD700;">star</span> - • ';
            }
            yearText += date.getFullYear();
            html +=
                '               <a is="emby-linkbutton" style="color: inherit; text-decoration: none;" target="_blank" href="' +
                    ((_w = providerIds["JellyseerrRoot"]) !== null && _w !== void 0 ? _w : "") +
                    "/" +
                    ((_x = item.SourceType) !== null && _x !== void 0 ? _x : "") +
                    "/" +
                    ((_y = providerIds["Jellyseerr"]) !== null && _y !== void 0 ? _y : "") +
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
    function createUpcomingCards(items) {
        let html = "";
        let index = 0;
        items.forEach((item) => {
            var _a, _b;
            const providerIds = (_a = item.ProviderIds) !== null && _a !== void 0 ? _a : {};
            const formattedDate = providerIds["FormattedDate"] || "";
            let contentType;
            let title;
            let secondaryInfo;
            let posterUrl;
            let cardClass;
            let cardScalableClass;
            let cardShapeClass = "overflowPortraitCard";
            let cardPadderClass = "cardPadder-overflowPortrait";
            const itemType = item.Type;
            if (itemType === "Episode" || providerIds["SonarrSeriesId"]) {
                contentType = "show";
                title = item.SeriesName || ((_b = item.Name) !== null && _b !== void 0 ? _b : "Unknown Series");
                secondaryInfo = providerIds["EpisodeInfo"] || "";
                posterUrl = providerIds["SonarrPoster"] || "";
                cardClass = "upcoming-show-card";
                cardScalableClass = "upcomingShowCard";
            }
            else if (itemType === "Movie" || providerIds["RadarrMovieId"]) {
                contentType = "movie";
                title = item.Name || "Unknown Movie";
                posterUrl = providerIds["RadarrPoster"] || "";
                cardClass = "upcoming-movie-card";
                cardScalableClass = "upcomingMovieCard";
            }
            else if (itemType === "MusicAlbum" || providerIds["LidarrArtistId"]) {
                contentType = "music";
                title = item.Name || "Unknown Album";
                secondaryInfo = item.Overview || "";
                posterUrl = providerIds["LidarrPoster"] || "";
                cardClass = "upcoming-music-card";
                cardScalableClass = "upcomingMusicCard";
                cardShapeClass = "overflowSquareCard";
                cardPadderClass = "cardPadder-square";
            }
            else if (itemType === "Book" || providerIds["ReadarrBookId"]) {
                contentType = "book";
                title = item.Name || "Unknown Book";
                secondaryInfo = item.Overview || "";
                posterUrl = providerIds["ReadarrPoster"] || "";
                cardClass = "upcoming-book-card";
                cardScalableClass = "upcomingBookCard";
            }
            else {
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
            }
            else {
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
    function getSectionClass(sectionInfo) {
        if (sectionInfo.Limit > 1) {
            return sectionInfo.Section + "-" + sectionInfo.AdditionalData.replace(" ", "-").replace(".", "-").replace("'", "");
        }
        else {
            return sectionInfo.Section;
        }
    }
    function loadHomeSection(pageEl, _apiClient, _user, userSettings, sectionInfo, options) {
        var _a;
        const sectionClass = getSectionClass(sectionInfo);
        console.log("Loading section: ." + sectionClass + ", could also be .section" + options.sectionIndex);
        const elem = pageEl.querySelector("." + sectionClass + '[data-page="' + window.HssPageMeta.Page + '"]');
        if (null !== elem) {
            let html = "";
            // Sentinel — replaced to {{layoutmanager_hook}}.A by scripts/replace-placeholders.mjs
            const layoutManager = {{layoutmanager_hook}}.A;
            html += '<div class="sectionTitleContainer sectionTitleContainer-cards padded-left">';
            let titleRoute = undefined;
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
                }
                catch (titleLinkError) {
                    console.warn("Home Screen Sections: failed to resolve title route for", sectionInfo.Section, titleLinkError);
                    titleRoute = undefined;
                }
            }
            const hasTitleLink = !!(titleRoute &&
                titleRoute !== "#" &&
                String(titleRoute).indexOf("undefined") === -1 &&
                String(titleRoute).length > 1);
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
            }
            else {
                html += '<h2 class="sectionTitle sectionTitle-cards">';
                html += sectionInfo.DisplayText;
                html += "</h2>";
            }
            html += "</div>";
            if (sectionInfo.ViewMode !== "Small") {
                html += '<div is="emby-scroller" class="padded-top-focusscale padded-bottom-focusscale" data-centerfocus="true">';
                html +=
                    '<div is="emby-itemscontainer" class="itemsContainer scrollSlider focuscontainer-x animatedScrollX" data-monitor="videoplayback,markplayed">';
            }
            else {
                html +=
                    '<div is="emby-itemscontainer" class="itemsContainer padded-left padded-right vertical-wrap focuscontainer-x" data-monitor="videoplayback,markplayed">';
            }
            html += "</div>";
            if (sectionInfo.ViewMode !== "Small") {
                html += "</div>";
            }
            elem.classList.add("hide");
            elem.innerHTML = html;
            const itemsContainer = elem.querySelector(".itemsContainer");
            if (itemsContainer !== null) {
                if (sectionInfo.ContainerClass !== undefined) {
                    itemsContainer.classList.add(sectionInfo.ContainerClass);
                }
                // Sentinel — replaced to {{cardbuilder_hook}}.default
                const cardBuilder = {{cardbuilder_hook}}.default;
                const cardSettings = {
                    ViewMode: sectionInfo.ViewMode,
                    DisplayTitleText: sectionInfo.DisplayTitleText,
                    ShowDetailsMenu: sectionInfo.ShowDetailsMenu,
                };
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                itemsContainer.fetchData = getHomeScreenSectionFetchFn(_apiClient.serverId(), sectionInfo, u.A, userSettings);
                const getBackdropShape = y.UI;
                const getPortraitShape = y.xK;
                const getSquareShape = y.zP;
                let getShapeFn = getBackdropShape;
                if (cardSettings.ViewMode === "Portrait") {
                    getShapeFn = getPortraitShape;
                }
                else if (cardSettings.ViewMode === "Square") {
                    getShapeFn = getSquareShape;
                }
                else if (cardSettings.ViewMode === "Backdrop") {
                    getShapeFn = getBackdropShape;
                }
                const imageHelper = b.Ay;
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                itemsContainer.getItemsHtml = getHomeScreenSectionItemsHtmlFn(userSettings.useEpisodeImagesInNextUpAndResume(), (_a = options.enableOverflow) !== null && _a !== void 0 ? _a : true, sectionInfo.Section, cardBuilder, getShapeFn, imageHelper, p.appRouter, cardSettings);
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                itemsContainer.parentContainer = elem;
            }
        }
        return Promise.resolve();
    }
    function getHomeScreenSectionsMeta(_apiClient) {
        return _apiClient.getJSON(_apiClient.getUrl("HomeScreen/Meta"));
    }
    function isUserUsingHomeScreenSections(pluginMeta, _userSettings) {
        try {
            if (pluginMeta && pluginMeta.AllowUserOverride === true) {
                const data = _userSettings && _userSettings.getData ? _userSettings.getData() : null;
                if (data && data.CustomPrefs && data.CustomPrefs["useModularHome"] !== undefined) {
                    return data.CustomPrefs["useModularHome"] === "true";
                }
            }
            return !!(pluginMeta && pluginMeta.Enabled);
        }
        catch (_e) {
            return false;
        }
    }
    function uuidv4() {
        return "10000000-1000-4000-8000-100000000000".replace(/[018]/g, (c) => (+c ^ (crypto.getRandomValues(new Uint8Array(1))[0] & (15 >> (+c / 4)))).toString(16));
    }
    const _this = this;
    return getHomeScreenSectionsMeta(apiClient)
        .then((hssMeta) => {
        var _a, _b;
        const useHss = isUserUsingHomeScreenSections(hssMeta, userSettings);
        if (!useHss) {
            return _this.originalLoadSections(elem, apiClient, user, userSettings);
        }
        if (page !== null) {
            window.HssPageMeta["Page"] = page;
        }
        else {
            window.HssPageMeta = {
                UsePagination: (_a = hssMeta.PaginationEnabled) !== null && _a !== void 0 ? _a : false,
                Page: 1,
                ResultsPerPage: (_b = hssMeta.NumResultsPerPage) !== null && _b !== void 0 ? _b : 20,
                LastScrollHeight: 0,
                ScrollThreshold: 10,
                PageHash: uuidv4(),
            };
            window.HssPageCache = {
                elem: elem,
                apiClient: apiClient,
                user: user,
                userSettings: userSettings,
            };
            if (typeof window.HssScrollHandler === "function") {
                window.removeEventListener("scroll", window.HssScrollHandler);
            }
            window.HssScrollHandler = function () {
                const scrollPosition = window.scrollY + window.innerHeight;
                const windowHeight = getDocHeight();
                if (window.HssPageMeta.Finished !== true &&
                    window.HssPageMeta.IsLoading !== true &&
                    scrollPosition > windowHeight - window.HssPageMeta.ScrollThreshold &&
                    window.HssPageMeta.LastScrollHeight < window.scrollY) {
                    window.HssPageMeta["IsLoading"] = true;
                    const indicator = document.querySelector("#hssLoadingIndicator");
                    if (indicator) {
                        indicator.style.display = "block";
                    }
                    let winHeight = getDocHeight();
                    window.scroll(0, winHeight - (window.innerHeight + window.HssPageMeta.ScrollThreshold));
                    window.HssPageMeta["LastScrollHeight"] = window.scrollY;
                    window.HssPageMeta["LastWindowHeight"] = winHeight;
                    window.HssPageMeta["ScrollFixerHandle"] = window.setInterval(function () {
                        window.scroll(0, window.HssPageMeta.LastScrollHeight);
                        if (getDocHeight() > window.HssPageMeta.LastWindowHeight) {
                            clearInterval(window.HssPageMeta.ScrollFixerHandle);
                            window.HssPageMeta.ScrollFixerHandle = undefined;
                        }
                    }, 1);
                    void _this
                        .loadSections(window.HssPageCache.elem, window.HssPageCache.apiClient, window.HssPageCache.user, window.HssPageCache.userSettings, window.HssPageMeta.Page + 1)
                        .then(function () {
                        const ind = document.querySelector("#hssLoadingIndicator");
                        if (ind) {
                            ind.style.display = "none";
                        }
                        window.HssPageMeta["IsLoading"] = false;
                        if (window.HssPageMeta.ScrollFixerHandle) {
                            clearInterval(window.HssPageMeta.ScrollFixerHandle);
                        }
                    });
                }
                function getDocHeight() {
                    const D = document;
                    return Math.max(D.body.scrollHeight, D.documentElement.scrollHeight, D.body.offsetHeight, D.documentElement.offsetHeight, D.body.clientHeight, D.documentElement.clientHeight);
                }
            };
            window.addEventListener("scroll", window.HssScrollHandler);
        }
        const getSectionsData = {
            UserId: apiClient.getCurrentUserId(),
            Language: localStorage.getItem(apiClient.getCurrentUserId() + "-language"),
        };
        if (window.HssPageMeta.UsePagination) {
            getSectionsData["Page"] = window.HssPageMeta.Page;
            getSectionsData["NumResultsPerPage"] = window.HssPageMeta.ResultsPerPage;
            getSectionsData["PageHash"] = window.HssPageMeta.PageHash;
        }
        else {
            window.HssPageMeta["Finished"] = true;
        }
        const getSectionsUrl = apiClient.getUrl("HomeScreen/Sections", getSectionsData);
        return apiClient.getJSON(getSectionsUrl).then(function (response) {
            if (response.TotalRecordCount === 0 && window.HssPageMeta.Page > 1) {
                window.HssPageMeta.Finished = true;
                return function (_elem, _apiClient, _user, _userSettings) { };
            }
            return function (elemArg, apiClientArg, userArg, userSettingsArg) {
                // Keep the original obfuscated generator shape behaviourally identical
                // but expressed with async/await for type safety — the outer contract
                // (returns Promise that resolves after sections are rendered) is preserved.
                return (async () => {
                    const var44 = response;
                    const options = {
                        enableOverflow: true,
                    };
                    let var44_3 = "";
                    const var44_4 = [];
                    if (void 0 !== var44.Items) {
                        const existingContainer = document.querySelector(".homeSectionsContainer");
                        let existingSections = 0;
                        if (existingContainer !== null) {
                            existingSections = existingContainer.children.length;
                        }
                        for (let var44_5 = 0; var44_5 < var44.TotalRecordCount; var44_5++) {
                            const var44_6 = getSectionClass(var44.Items[var44_5]);
                            void var44.Items[var44_5].Limit;
                            var44_3 +=
                                '<div data-page="' +
                                    String(window.HssPageMeta.Page) +
                                    '" style="order:' +
                                    String(var44.Items[var44_5].OrderIndex + 1000 * (window.HssPageMeta.Page - 1)) +
                                    ';" class="verticalSection ' +
                                    var44_6 +
                                    " section" +
                                    String(existingSections + var44_5) +
                                    '"></div>';
                        }
                        if (window.HssPageMeta.Page !== 1) {
                            const tempContainer = document.createElement("div");
                            tempContainer.innerHTML = var44_3;
                            while (tempContainer.firstChild) {
                                elemArg.appendChild(tempContainer.firstChild);
                            }
                        }
                        else {
                            const spinnerHtml = '<div id="hssLoadingIndicator" class="verticalSection" style="order: 2147000000;margin-top:60px;margin-bottom:60px;display:none;"><div dir="ltr" class="docspinner mdl-spinner mdlSpinnerActive" style="position: relative;top: 0;left: calc(50vw - 1.5em);"><div class="mdl-spinner__layer mdl-spinner__layer-1"><div class="mdl-spinner__circle-clipper mdl-spinner__left"><div class="mdl-spinner__circle mdl-spinner__circleLeft"></div></div><div class="mdl-spinner__circle-clipper mdl-spinner__right"><div class="mdl-spinner__circle mdl-spinner__circleRight"></div></div></div><div class="mdl-spinner__layer mdl-spinner__layer-2"><div class="mdl-spinner__circle-clipper mdl-spinner__left"><div class="mdl-spinner__circle mdl-spinner__circleLeft"></div></div><div class="mdl-spinner__circle-clipper mdl-spinner__right"><div class="mdl-spinner__circle mdl-spinner__circleRight"></div></div></div><div class="mdl-spinner__layer mdl-spinner__layer-3"><div class="mdl-spinner__circle-clipper mdl-spinner__left"><div class="mdl-spinner__circle mdl-spinner__circleLeft"></div></div><div class="mdl-spinner__circle-clipper mdl-spinner__right"><div class="mdl-spinner__circle mdl-spinner__circleRight"></div></div></div><div class="mdl-spinner__layer mdl-spinner__layer-4"><div class="mdl-spinner__circle-clipper mdl-spinner__left"><div class="mdl-spinner__circle mdl-spinner__circleLeft"></div></div><div class="mdl-spinner__circle-clipper mdl-spinner__right"><div class="mdl-spinner__circle mdl-spinner__circleRight"></div></div></div></div></div>';
                            elemArg.innerHTML = spinnerHtml + var44_3;
                        }
                        if (!elemArg.classList.contains("homeSectionsContainer")) {
                            elemArg.classList.add("homeSectionsContainer");
                        }
                        if (var44.TotalRecordCount > 0) {
                            for (let var44_7 = 0; var44_7 < var44.Items.length; var44_7++) {
                                const sectionInfo = var44.Items[var44_7];
                                options.sectionIndex = var44_7;
                                var44_4.push(loadHomeSection(elemArg, apiClientArg, 0, userSettingsArg, sectionInfo, options));
                            }
                        }
                    }
                    return var44.TotalRecordCount > 0
                        ? Promise.all(var44_4).then(function () {
                            const var134_2 = { refresh: true };
                            const var134_3 = elemArg.querySelectorAll('[data-page="' + String(window.HssPageMeta.Page) + '"] .itemsContainer');
                            const var134_4 = [];
                            Array.prototype.forEach.call(var134_3, function (param139) {
                                if (param139.resume) {
                                    var134_4.push(param139.resume(var134_2));
                                }
                            });
                            return Promise.all(var134_4);
                        })
                        : (() => {
                            var _a;
                            const isAdmin = (_a = userArg.Policy) === null || _a === void 0 ? void 0 : _a.IsAdministrator;
                            const var44_9 = isAdmin
                                ? s.Ay.translate("NoCreatedLibraries", '<br><a id="button-createLibrary" class="button-link">', "</a>")
                                : s.Ay.translate("AskAdminToCreateLibrary");
                            var44_3 += '<div class="centerMessage padded-left padded-right">';
                            var44_3 += "<h2>" + s.Ay.translate("MessageNothingHere") + "</h2>";
                            var44_3 += "<p>" + var44_9 + "</p>";
                            var44_3 += "</div>";
                            elemArg.innerHTML = var44_3;
                            const var44_10 = elemArg.querySelector("#button-createLibrary");
                            if (var44_10) {
                                var44_10.addEventListener("click", function () {
                                    l.default.navigate("dashboard/libraries");
                                });
                            }
                            return undefined;
                        })();
                })();
            };
        }, function (error) {
            console.error("Error fetching sections with HSS, defaulting back to Jellyfin:", error);
            return _this.originalLoadSections(elem, apiClient, user, userSettings);
        });
    }, function (_err) {
        return _this.originalLoadSections(elem, apiClient, user, userSettings);
    });
}
