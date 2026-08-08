/**
 * HomeScreenSections static handlers — injected via /HomeScreen/home-screen-sections.js.
 *
 * Strictly typed against @jellyfin/sdk (server DTOs) and src-web/types/jellyfin-web.d.ts
 * (web-client globals). Runs in the jellyfin-web page context, not the plugin server.
 */
"use strict";
if (typeof globalThis["HomeScreenSectionsHandler"] === "undefined") {
    const HomeScreenSectionsHandlerImpl = {
        init() {
            const MutationObserverCtor = window.MutationObserver || window["WebKitMutationObserver"];
            const myObserver = new MutationObserverCtor(this.mutationHandler.bind(this));
            const observerConfig = { childList: true, characterData: true, attributes: true, subtree: true };
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            $("body").each(function () {
                myObserver.observe(this, observerConfig);
            });
        },
        mutationHandler(mutationRecords) {
            mutationRecords.forEach((mutation) => {
                if (mutation.addedNodes && mutation.addedNodes.length > 0) {
                    [].some.call(mutation.addedNodes, (addedNode) => {
                        // eslint-disable-next-line @typescript-eslint/no-explicit-any
                        if ($(addedNode).hasClass("discover-card")) {
                            // eslint-disable-next-line @typescript-eslint/no-explicit-any
                            $(addedNode).on("click", ".discover-requestbutton", HomeScreenSectionsHandlerImpl.clickHandler);
                        }
                        return false;
                    });
                }
            });
        },
        clickHandler(_event) {
            const target = this;
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            const $target = $(target);
            window.ApiClient.ajax({
                url: window.ApiClient.getUrl("HomeScreen/DiscoverRequest"),
                type: "POST",
                data: JSON.stringify({
                    UserId: window.ApiClient._currentUser.Id,
                    MediaType: $target.data("media-type"),
                    MediaId: $target.data("id"),
                }),
                contentType: "application/json; charset=utf-8",
                dataType: "json",
            }).then((response) => {
                const res = response;
                if ((res === null || res === void 0 ? void 0 : res.errors) && res.errors.length > 0) {
                    window.Dashboard.alert("Item request failed. Check browser logs for details.");
                    console.error("Item request failed. Response including errors:");
                    console.error(response);
                }
                else {
                    window.Dashboard.alert("Item successfully requested");
                }
            }, () => {
                window.Dashboard.alert("Item request failed");
            });
        },
    };
    // Expose globally under the legacy name
    globalThis["HomeScreenSectionsHandler"] = HomeScreenSectionsHandlerImpl;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    $(document).ready(() => {
        setTimeout(() => {
            HomeScreenSectionsHandlerImpl.init();
        }, 50);
    });
}
if (typeof globalThis["TopTenSectionHandler"] === "undefined") {
    const TopTenSectionHandlerImpl = {
        init() {
            const MutationObserverCtor = window.MutationObserver || window["WebKitMutationObserver"];
            const myObserver = new MutationObserverCtor(this.mutationHandler.bind(this));
            const observerConfig = { childList: true, characterData: true, attributes: true, subtree: true };
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            $("body").each(function () {
                myObserver.observe(this, observerConfig);
            });
        },
        mutationHandler(mutationRecords) {
            mutationRecords.forEach((mutation) => {
                if (mutation.addedNodes && mutation.addedNodes.length > 0) {
                    [].some.call(mutation.addedNodes, (addedNode) => {
                        // eslint-disable-next-line @typescript-eslint/no-explicit-any
                        if ($(addedNode).hasClass("card")) {
                            // eslint-disable-next-line @typescript-eslint/no-explicit-any
                            if ($(addedNode).parents(".top-ten").length > 0) {
                                const idxAttr = addedNode.getAttribute("data-index");
                                const index = parseInt(idxAttr !== null && idxAttr !== void 0 ? idxAttr : "0", 10);
                                addedNode.setAttribute("data-number", String(index + 1));
                            }
                        }
                        return false;
                    });
                }
            });
        },
    };
    globalThis["TopTenSectionHandler"] = TopTenSectionHandlerImpl;
    setTimeout(() => {
        TopTenSectionHandlerImpl.init();
    }, 50);
}
