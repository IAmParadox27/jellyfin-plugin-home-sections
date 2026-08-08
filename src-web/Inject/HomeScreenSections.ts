/**
 * HomeScreenSections static handlers — injected via /HomeScreen/home-screen-sections.js.
 *
 * Strictly typed against @jellyfin/sdk (server DTOs) and src-web/types/jellyfin-web.d.ts
 * (web-client globals). Runs in the jellyfin-web page context, not the plugin server.
 */
"use strict";

if (typeof (globalThis as unknown as Record<string, unknown>)["HomeScreenSectionsHandler"] === "undefined") {
    const HomeScreenSectionsHandlerImpl = {
        init(): void {
            const MutationObserverCtor: typeof MutationObserver =
                window.MutationObserver || (window as unknown as Record<string, typeof MutationObserver>)["WebKitMutationObserver"];
            const myObserver = new MutationObserverCtor(this.mutationHandler.bind(this) as MutationCallback);
            const observerConfig: MutationObserverInit = { childList: true, characterData: true, attributes: true, subtree: true };

            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            ($ as any)("body").each(function (this: Element): void {
                myObserver.observe(this, observerConfig);
            });
        },

        mutationHandler(mutationRecords: MutationRecord[]): void {
            mutationRecords.forEach((mutation: MutationRecord): void => {
                if (mutation.addedNodes && mutation.addedNodes.length > 0) {
                    ([] as unknown as { some: Array<HTMLElement>["some"] }).some.call(
                        mutation.addedNodes,
                        (addedNode: Node): boolean => {
                            // eslint-disable-next-line @typescript-eslint/no-explicit-any
                            if (($ as any)(addedNode).hasClass("discover-card")) {
                                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                                ($ as any)(addedNode).on("click", ".discover-requestbutton", HomeScreenSectionsHandlerImpl.clickHandler);
                            }
                            return false;
                        },
                    );
                }
            });
        },

        clickHandler(this: HTMLElement, _event: unknown): void {
            const target = this as HTMLElement & { dataset?: DOMStringMap } & { getAttribute?: (n: string) => string | null };
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            const $target: any = ($ as any)(target);
            window.ApiClient.ajax({
                url: window.ApiClient.getUrl("HomeScreen/DiscoverRequest"),
                type: "POST",
                data: JSON.stringify({
                    UserId: window.ApiClient._currentUser.Id,
                    MediaType: $target.data("media-type") as string,
                    MediaId: $target.data("id") as string,
                }),
                contentType: "application/json; charset=utf-8",
                dataType: "json",
            }).then(
                (response: unknown): void => {
                    const res = response as { errors?: unknown[] } | null;
                    if (res?.errors && res.errors.length > 0) {
                        window.Dashboard.alert("Item request failed. Check browser logs for details.");
                        console.error("Item request failed. Response including errors:");
                        console.error(response);
                    } else {
                        window.Dashboard.alert("Item successfully requested");
                    }
                },
                (): void => {
                    window.Dashboard.alert("Item request failed");
                },
            );
        },
    };

    // Expose globally under the legacy name
    (globalThis as unknown as Record<string, unknown>)["HomeScreenSectionsHandler"] = HomeScreenSectionsHandlerImpl;

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    ($ as any)(document).ready((): void => {
        setTimeout((): void => {
            HomeScreenSectionsHandlerImpl.init();
        }, 50);
    });
}

if (typeof (globalThis as unknown as Record<string, unknown>)["TopTenSectionHandler"] === "undefined") {
    const TopTenSectionHandlerImpl = {
        init(): void {
            const MutationObserverCtor: typeof MutationObserver =
                window.MutationObserver || (window as unknown as Record<string, typeof MutationObserver>)["WebKitMutationObserver"];
            const myObserver = new MutationObserverCtor(this.mutationHandler.bind(this) as MutationCallback);
            const observerConfig: MutationObserverInit = { childList: true, characterData: true, attributes: true, subtree: true };

            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            ($ as any)("body").each(function (this: Element): void {
                myObserver.observe(this, observerConfig);
            });
        },

        mutationHandler(mutationRecords: MutationRecord[]): void {
            mutationRecords.forEach((mutation: MutationRecord): void => {
                if (mutation.addedNodes && mutation.addedNodes.length > 0) {
                    ([] as unknown as { some: Array<HTMLElement>["some"] }).some.call(
                        mutation.addedNodes,
                        (addedNode: Node): boolean => {
                            // eslint-disable-next-line @typescript-eslint/no-explicit-any
                            if (($ as any)(addedNode).hasClass("card")) {
                                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                                if (($ as any)(addedNode).parents(".top-ten").length > 0) {
                                    const idxAttr: string | null = (addedNode as HTMLElement).getAttribute("data-index");
                                    const index: number = parseInt(idxAttr ?? "0", 10);
                                    (addedNode as HTMLElement).setAttribute("data-number", String(index + 1));
                                }
                            }
                            return false;
                        },
                    );
                }
            });
        },
    };

    (globalThis as unknown as Record<string, unknown>)["TopTenSectionHandler"] = TopTenSectionHandlerImpl;

    setTimeout((): void => {
        TopTenSectionHandlerImpl.init();
    }, 50);
}

export {};