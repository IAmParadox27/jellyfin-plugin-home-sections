async function test(elem, apiClient, user, userSettings) {
    if (!isHomePage()) {
        return;
    }

    function isHomePage() {
        const hasIndexPageId = document.getElementById('indexPage') !== null;
        const hasHomePageClass = document.querySelector('.page.homePage') !== null;
        const hasSectionsDiv = document.querySelector('.sections') !== null;
        const hasPageRole = document.querySelector('[data-role="page"]') !== null;

        return hasIndexPageId && hasHomePageClass && hasSectionsDiv && hasPageRole;
    }

    function getHomeScreenSectionFetchFn(serverId, sectionInfo, serverConnections, _userSettings) {
        return function() {
            var __userSettings = _userSettings;
            
            //var _apiClient = serverConnections.getApiClient(serverId);
            var queryParams = {
                UserId: apiClient.getCurrentUserId(),
                AdditionalData: sectionInfo.AdditionalData,
                Language: localStorage.getItem(apiClient.getCurrentUserId() + '-language')
            };
            
            if (sectionInfo.Section === 'NextUp') {
                var cutoffDate = new Date();
                cutoffDate.setDate(cutoffDate.getDate() - _userSettings.maxDaysForNextUp());
                
                queryParams.NextUpDateCutoff = cutoffDate.toISOString();
                queryParams.EnableRewatching = _userSettings.enableRewatchingInNextUp();
            }
            
            var getUrl = apiClient.getUrl("HomeScreen/Section/" + sectionInfo.Section, queryParams);
            return apiClient.getJSON(getUrl);
        }
    }
    
    function getHomeScreenSectionItemsHtmlFn(useEpisodeImages, enableOverflow, sectionKey, cardBuilder, getShapeFn, additionalSettings) {
        if (sectionKey === "DiscoverMovies" || sectionKey === "DiscoverTV" || sectionKey === "Discover") {
            return createDiscoverCards;
        }
        
        return function(items) {
            return cardBuilder.getCardsHtml({
                items: items,
                preferThumb: additionalSettings.ViewMode === 'Portrait' ? null : 'auto',
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
                lines: additionalSettings.DisplayTitleText ? (sectionKey === "MyMedia" ? 1 : 2) : 0
            });
        }
    }
    
    function createDiscoverCards(items) {
        var html = '';
        
        var index = 0;
        items.forEach(function (item) {
            html += '<div class="card overflowPortraitCard card-hoverable card-withuserdata discover-card" data-index="' + index + '">';
            html += '   <div class="cardBox cardBox-bottompadded">';
            html += '       <div class="cardScalable discoverCard-' + item.SourceType + '">';
            html += '           <div class="cardPadder cardPadder-overflowPortrait lazy-hidden-children"></div>';
            html += '           <canvas aria-hidden="true" width="20" height="20" class="blurhash-canvas lazy-hidden"></canvas>';
            html += '           <a target="_blank" href="' + item.ProviderIds.JellyseerrRoot + '/' + item.SourceType + '/' + item.ProviderIds.Jellyseerr + '" class="cardImageContainer coveredImage cardContent itemAction lazy blurhashed lazy-image-fadein-fast" aria-label="" style="background-image: url(' + "'https://image.tmdb.org/t/p/w600_and_h900_bestv2" + item.ProviderIds.JellyseerrPoster + "'" +')"></a>';
            html += '           <div class="cardOverlayContainer itemAction" data-action="link">';
            html += '               <a target="_blank" href="' + item.ProviderIds.JellyseerrRoot + '/' + item.SourceType + '/' + item.ProviderIds.Jellyseerr + '" class="cardImageContainer"></a>';
            html += '               <div class="cardOverlayButton-br flex">';
            html += '                   <button is="discover-requestbutton" type="button" data-action="none" class="discover-requestbutton cardOverlayButton cardOverlayButton-hover itemAction paper-icon-button-light emby-button" data-id="' + item.ProviderIds.Jellyseerr + '" data-media-type="' + item.SourceType + '">';
            html += '                       <span class="material-icons cardOverlayButtonIcon cardOverlayButtonIcon-hover add" aria-hidden="true"></span>';
            html += '                   </button>';
            html += '               </div>';
            html += '           </div>';
            html += '       </div>';
            html += '       <div class="cardText cardTextCentered cardText-first">';
            html += '           <bdi>';
            html += '               <a target="_blank" href="' + item.ProviderIds.JellyseerrRoot + '/' + item.SourceType + '/' + item.ProviderIds.Jellyseerr + '" class="itemAction textActionButton" title="' + item.Name + '" data-action="link">' + item.Name + '</a>';
            html += '           </bdi>';
            html += '       </div>';
            html += '       <div class="cardText cardTextCentered cardText-secondary">';
            html += '           <bdi>';

            var date = new Date(item.PremiereDate);
            
            html += '               <a target="_blank" href="' + item.ProviderIds.JellyseerrRoot + '/' + item.SourceType + '/' + item.ProviderIds.Jellyseerr + '" class="itemAction textActionButton" title="' + date.getFullYear() + '" data-action="link">' + date.getFullYear() + '</a>';
            html += '           </bdi>';
            html += '       </div>';
            html += '   </div>';
            html += '</div>';
            index++;
        });
        
        return html;
    }
    
    function loadHomeSection(page, apiClient, user, userSettings, sectionInfo, options) {
        var sectionClass = sectionInfo.Section;
        if (sectionInfo.Limit > 1) {
            sectionClass += "-" + sectionInfo.AdditionalData;
        }
        var var5_, var6_, var7_, var8_, elem = page.querySelector("." + sectionClass);
        if (null !== elem) {
            var html = "";
            var layoutManager = {{layoutmanager_hook}}.A;
            html += '<div class="sectionTitleContainer sectionTitleContainer-cards padded-left">';
            if (!layoutManager.tv && sectionInfo.Route !== undefined) {
                var route = undefined;
                if (sectionInfo.OriginalPayload !== undefined) {
                    route = p.appRouter.getRouteUrl(sectionInfo.OriginalPayload, {
                        serverId: apiClient.serverId()
                    });
                } else {
                    route = p.appRouter.getRouteUrl(sectionInfo.Route, {
                        serverId: apiClient.serverId()
                    })
                }

                html += '<a is="emby-linkbutton" href="' + route + '" class="button-flat button-flat-mini sectionTitleTextButton">';
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
            html += '<div is="emby-scroller" class="padded-top-focusscale padded-bottom-focusscale" data-centerfocus="true">';
            html += '<div is="emby-itemscontainer" class="itemsContainer scrollSlider focuscontainer-x" data-monitor="videoplayback,markplayed">';
            html += "</div>";
            html += "</div>";
            elem.classList.add("hide");
            elem.innerHTML = html;
            
            var var13_, var14_, itemsContainer = elem.querySelector(".itemsContainer");
            
            if (itemsContainer !== null) {
                if (sectionInfo.ContainerClass !== undefined) {
                    itemsContainer.classList.add(sectionInfo.ContainerClass);
                }
                
                var cardBuilder = {{cardbuilder_hook}}.default;
                
                var cardSettings = {
                    ViewMode: sectionInfo.ViewMode,
                    DisplayTitleText: sectionInfo.DisplayTitleText,
                    ShowDetailsMenu: sectionInfo.ShowDetailsMenu
                }
                
                itemsContainer.fetchData = getHomeScreenSectionFetchFn(apiClient.serverId(), sectionInfo, u.A, userSettings);
                
                var getBackdropShape = y.UI;
                var getPortraitShape = y.xK;
                var getSquareShape = y.zP;
                
                var getShapeFn = getBackdropShape;
                if (cardSettings.ViewMode === 'Portrait')
                {
                    getShapeFn = getPortraitShape;
                }
                else if (cardSettings.ViewMode === 'Square')
                {
                    getShapeFn = getSquareShape;
                }
                
                itemsContainer.getItemsHtml = getHomeScreenSectionItemsHtmlFn(userSettings.useEpisodeImagesInNextUpAndResume(), options.enableOverflow, sectionInfo.Section, cardBuilder, getShapeFn, cardSettings);
                itemsContainer.parentContainer = elem;
            }
        }
        return Promise.resolve()
    }
    
    async function isUserUsingHomeScreenSections(_userSettings, _apiClient) {
        var pluginConfig = await _apiClient.getJSON(_apiClient.getUrl("HomeScreen/Meta"));
        
        if (pluginConfig.AllowUserOverride === true) {
            if (_userSettings && _userSettings.getData() && _userSettings.getData().CustomPrefs && _userSettings.getData().CustomPrefs.useModularHome !== undefined) {
                return _userSettings.getData().CustomPrefs.useModularHome === "true";
            }
        }
        
        return pluginConfig.Enabled;
    }
    
    if (await isUserUsingHomeScreenSections(userSettings, apiClient)) {
        return function(elem, apiClient, user, userSettings) {
            var var39_, var39_3, var39_4;
            return var39_ = this, void 0, var39_4 = function() {
                var var44_, options, var44_3, var44_4, var44_5, var44_6, var44_7, sectionInfo, var44_9, var44_10, var44_11;
                return function(param45_, param45_2) {
                    var var46_, var47_, var48_, var49_ = {
                            label: 0,
                            sent: function() {
                                if (1 & var48_[0]) throw var48_[1];
                                return var48_[1]
                            },
                            trys: [],
                            ops: []
                        },
                        var58_ = Object.create(("function" == typeof Iterator ? Iterator : Object).prototype);
                    return var58_.next = fn69_(0), var58_.throw = fn69_(1), var58_.return = fn69_(2), "function" == typeof Symbol && (var58_[Symbol.iterator] = function() {
                        return this
                    }), var58_;

                    function fn69_(param69_) {
                        return function(param70_) {
                            return function(param71_) {
                                if (var46_) throw TypeError("Generator is already executing.");
                                for (; var58_ && (var58_ = 0, param71_[0] && (var49_ = 0)), var49_;) try {
                                    if (var46_ = 1, var47_ && (var48_ = 2 & param71_[0] ? var47_.return : param71_[0] ? var47_.throw || ((var48_ = var47_.return) && var48_.call(var47_), 0) : var47_.next) && !(var48_ = var48_.call(var47_, param71_[1])).done) return var48_;
                                    switch (var47_ = 0, var48_ && (param71_ = [2 & param71_[0], var48_.value]), param71_[0]) {
                                        case 0:
                                        case 1:
                                            var48_ = param71_;
                                            break;
                                        case 4:
                                            return var49_.label++, {
                                                value: param71_[1],
                                                done: !1
                                            };
                                        case 5:
                                            var49_.label++, var47_ = param71_[1], param71_ = [0];
                                            continue;
                                        case 7:
                                            param71_ = var49_.ops.pop(), var49_.trys.pop();
                                            continue;
                                        default:
                                            if (!((var48_ = (var48_ = var49_.trys).length > 0 && var48_[var48_.length - 1]) || 6 !== param71_[0] && 2 !== param71_[0])) {
                                                var49_ = 0;
                                                continue
                                            }
                                            if (3 === param71_[0] && (!var48_ || param71_[1] > var48_[0] && param71_[1] < var48_[3])) {
                                                var49_.label = param71_[1];
                                                break
                                            }
                                            if (6 === param71_[0] && var49_.label < var48_[1]) {
                                                var49_.label = var48_[1], var48_ = param71_;
                                                break
                                            }
                                            if (var48_ && var49_.label < var48_[2]) {
                                                var49_.label = var48_[2], var49_.ops.push(param71_);
                                                break
                                            }
                                            var48_[2] && var49_.ops.pop(), var49_.trys.pop();
                                            continue
                                    }
                                    param71_ = param45_2.call(param45_, var49_)
                                } catch (let110_) {
                                    param71_ = [6, let110_], var47_ = 0
                                } finally {
                                    var46_ = var48_ = 0
                                }
                                if (5 & param71_[0]) throw param71_[1];
                                return {
                                    value: param71_[0] ? param71_[1] : void 0,
                                    done: !0
                                }
                            }([param69_, param70_])
                        }
                    }
                }(this, (function(param120_) {
                    switch (param120_.label) {
                        case 0:
                            var var123_, var123_2, var123_3;
                            return [4, (var123_ = apiClient, var123_2 = {
                                UserId: apiClient.getCurrentUserId(),
                                Language: localStorage.getItem(apiClient.getCurrentUserId() + '-language')
                            }, var123_3 = var123_.getUrl("HomeScreen/Sections", var123_2), var123_.getJSON(var123_3))];
                        case 1:
                            if (var44_ = param120_.sent(), options = {
                                enableOverflow: !0
                            }, var44_3 = "", var44_4 = [], void 0 !== var44_.Items) {
                                for (var44_5 = 0; var44_5 < var44_.TotalRecordCount; var44_5++) var44_6 = var44_.Items[var44_5].Section, var44_.Items[var44_5].Limit > 1 && (var44_6 += "-" + var44_.Items[var44_5].AdditionalData), var44_3 += '<div class="verticalSection ' + var44_6 + '"></div>';
                                if (elem.innerHTML = var44_3, elem.classList.add("homeSectionsContainer"), var44_.TotalRecordCount > 0)
                                    for (var44_7 = 0; var44_7 < var44_.Items.length; var44_7++) sectionInfo = var44_.Items[var44_7], var44_4.push(loadHomeSection(elem, apiClient, 0, userSettings, sectionInfo, options))
                            }
                            return var44_.TotalRecordCount > 0 ? [2, Promise.all(var44_4).then((function() {
                                var var134_2, var134_3, var134_4;
                                return var134_2 = {
                                    refresh: !0
                                }, var134_3 = elem.querySelectorAll(".itemsContainer"), var134_4 = [], Array.prototype.forEach.call(var134_3, (function(param139_) {
                                    param139_.resume && var134_4.push(param139_.resume(var134_2))
                                })), Promise.all(var134_4)
                            }))] : (var44_9 = (null === (var44_11 = user.Policy) || void 0 === var44_11 ? void 0 : var44_11.IsAdministrator) ? s.Ay.translate("NoCreatedLibraries", '<br><a id="button-createLibrary" class="button-link">', "</a>") : s.Ay.translate("AskAdminToCreateLibrary"), var44_3 += '<div class="centerMessage padded-left padded-right">', var44_3 += "<h2>" + s.Ay.translate("MessageNothingHere") + "</h2>", var44_3 += "<p>" + var44_9 + "</p>", var44_3 += "</div>", elem.innerHTML = var44_3, (var44_10 = elem.querySelector("#button-createLibrary")) && var44_10.addEventListener("click", (function() {
                                l.default.navigate("dashboard/libraries")
                            })), [2])
                    }
                }))
            }, new(var39_3 = void 0, var39_3 = Promise)((function(param160_, param160_2) {
                function fn161_(param161_) {
                    try {
                        fn175_(var39_4.next(param161_))
                    } catch (let164_) {
                        param160_2(let164_)
                    }
                }

                function fn168_(param168_) {
                    try {
                        fn175_(var39_4.throw(param168_))
                    } catch (let171_) {
                        param160_2(let171_)
                    }
                }

                function fn175_(param175_) {
                    var var176_;
                    param175_.done ? param160_(param175_.value) : ((var176_ = param175_.value) instanceof var39_3 ? var176_ : new var39_3((function(param181_) {
                        param181_(var176_)
                    }))).then(fn161_, fn168_)
                }
                fn175_((var39_4 = var39_4.apply(var39_, [])).next())
            }))
        }(elem, apiClient, user, userSettings);
    } else {
        return this.originalLoadSections(elem, apiClient, user, userSettings);
    }
}