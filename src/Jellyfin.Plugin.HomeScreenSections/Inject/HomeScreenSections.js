'use strict';

if (typeof HomeScreenSectionsHandler == 'undefined') {
    const HomeScreenSectionsHandler = {
        init: function() {
            var MutationObserver = window.MutationObserver || window.WebKitMutationObserver;
            var myObserver = new MutationObserver(this.mutationHandler);
            var observerConfig = {childList: true, characterData: true, attributes: true, subtree: true};

            $("body").each(function () {
                myObserver.observe(this, observerConfig);
            });
        },
        mutationHandler: function (mutationRecords) {
            mutationRecords.forEach(function (mutation) {
                if (mutation.addedNodes && mutation.addedNodes.length > 0) {
                    [].some.call(mutation.addedNodes, function (addedNode) {
                        if ($(addedNode).hasClass('discover-card')) {
                            $(addedNode).on('click', '.discover-requestbutton', HomeScreenSectionsHandler.clickHandler);
                        }
                    });
                }
            });
        },
        clickHandler: function(event) {
            $.ajax({
                url: window.ApiClient.getUrl("HomeScreen/DiscoverRequest"),
                method: "POST",
                data: JSON.stringify({
                    UserId: window.ApiClient._currentUser.Id,
                    MediaType: $(this).data('media-type'),
                    MediaId: $(this).data('id'),
                }),
                contentType: 'application/json; charset=utf-8',
                dataType: 'json'
            })
        }
    };
    
    $(document).ready(function () {
        setTimeout(function () {
            HomeScreenSectionsHandler.init();
        }, 50);
    });
}

if (typeof TopTenSectionHandler == 'undefined') {
    const TopTenSectionHandler = {
        init: function () {
            var MutationObserver = window.MutationObserver || window.WebKitMutationObserver;
            var myObserver = new MutationObserver(this.mutationHandler);
            var observerConfig = {childList: true, characterData: true, attributes: true, subtree: true};

            $("body").each(function () {
                myObserver.observe(this, observerConfig);
            });
        },
        mutationHandler: function (mutationRecords) {
            mutationRecords.forEach(function (mutation) {
                if (mutation.addedNodes && mutation.addedNodes.length > 0) {
                    [].some.call(mutation.addedNodes, function (addedNode) {
                        if ($(addedNode).hasClass('card')) {
                            if ($(addedNode).parents('.top-ten').length > 0) {
                                var index = parseInt($(addedNode).attr('data-index'));
                                $(addedNode).attr('data-number', index + 1);
                            }
                        }
                    });
                }
            });
        }
    }

    setTimeout(function () {
        TopTenSectionHandler.init();
    }, 50);
}
