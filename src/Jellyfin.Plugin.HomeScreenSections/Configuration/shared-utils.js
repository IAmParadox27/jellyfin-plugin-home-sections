(function(){
    'use strict';

    function parseBoolean(val){
        if (typeof val === 'boolean') return val;
        if (val == null) return false;
        if (typeof val === 'number') return val !== 0;
        var s = String(val).trim().toLowerCase();
        if (['true','1','yes','on'].indexOf(s) !== -1) return true;
        if (['false','0','no','off',''].indexOf(s) !== -1) return false;
        return !!val;
    }

    function escapeHtml(str){
        return String(str)
            .replace(/&/g,'&amp;')
            .replace(/</g,'&lt;')
            .replace(/>/g,'&gt;')
            .replace(/"/g,'&quot;')
            .replace(/'/g,'&#39;');
    }
    function escapeAttr(str){
        return escapeHtml(str).replace(/`/g,'&#96;');
    }

    function buildDropdownOptions(option, current){
        if (!option || !Array.isArray(option.DropdownOptions)) return '';
        return option.DropdownOptions.map(function(v, i){
            var lbl = (option.DropdownLabels && option.DropdownLabels[i]) || v;
            return '<option value="'+escapeAttr(v)+'"'+(v===current?' selected':'')+'>'+escapeHtml(lbl)+'</option>';
        }).join('');
    }

    function sanitizePattern(pat){
        if (!pat) return pat;
        var s = String(pat);
        s = s.replace(/_\-/g,'_-');
        s = s.replace(/\[_]?-\]/g,function(m){ return m.replace('-', '\\-'); });
        s = s.replace(/-\]/g,'\\-]');
        s = s.replace(/_-/g,'_\\-');
        return s;
    }

    function validateInputs(container){
        var result = { valid: true, message: '' };
        if(!container) return result;

        var firstInvalid = container.querySelector('.pluginConfig:invalid');
        if (firstInvalid) {
            result.valid = false;
            result.message = firstInvalid.validationMessage || 'Please correct the highlighted fields';
        }
        return result;
    }

    function attachLiveValidation(container, opts){
        if(!container) return;
        var onState = (typeof opts === 'function') ? opts : (opts && opts.onState);
        var handler = function(){
            var r = validateInputs(container);
            if(onState) onState(r);
        };
        container.addEventListener('input', handler, true);
        container.addEventListener('change', handler, true);
        container.addEventListener('invalid', handler, true);
        
        container.addEventListener('input', function(e) {
            if (e.target.matches('.pluginConfig')) {
                e.target.classList.toggle('invalid', !e.target.validity.valid);
            }
        }, true);
    }

    function attachUnifiedValidation(container, options){
        if(!container) return;
        options = options || {};
        var toast = options.toast !== false;
        var color = options.color !== false;
        attachLiveValidation(container, options);
        var shownFor = new WeakSet();
        container.querySelectorAll('.pluginConfig').forEach(function(el){
            el.addEventListener('blur', function(){
                if(typeof el.checkValidity === 'function'){
                    var valid = el.checkValidity();
                    if(color){
                        if(!valid) el.classList.add('invalid'); else el.classList.remove('invalid');
                    }
                    if(!valid && toast && !shownFor.has(el)){
                        var msg = el.validationMessage || 'Invalid value';
                        try { if(window.Dashboard && typeof Dashboard.alert==='function') Dashboard.alert(msg); } catch(_){ /* noop */ }
                        shownFor.add(el);
                    }
                }
            });
            el.addEventListener('input', function(){ if(color){ if(el.checkValidity && el.checkValidity()) el.classList.remove('invalid'); }});
        });
    }

    function attrIf(name, val){
        return (val===undefined || val===null || val==='' || val===false) ? '' : ' '+name+'="'+escapeAttr(String(val))+'"';
    }

    function buildAdminOptionInput(opt, value){
        if(!opt) return '';
        var t=(opt.Type||'').toLowerCase();
        var key=escapeAttr(opt.Key||'');
        var base=' data-config-control="true" data-config-key="'+key+'" data-config-type="'+escapeAttr(opt.Type||'')+'"';
        
        if(t==='checkbox'){
            return '<label class="emby-checkbox-label" style="width:0;">'
                +'<input is="emby-checkbox" type="checkbox" class="emby-checkbox pluginConfig"'+base+(parseBoolean(value)?' checked':'')+'>'
                +'<span class="checkboxLabel"></span>'
                // +'<span class="checkboxOutline"><span class="material-icons checkboxIcon checkboxIcon-checked check"></span><span class="material-icons checkboxIcon checkboxIcon-unchecked"></span></span>'
                +'</label>';
        }
        if(t==='dropdown'){
            return '<select is="emby-select" class="pluginConfig emby-select-withcolor emby-select"'+base+attrIf('required', opt.Required)+'>'+buildDropdownOptions(opt, value)+'</select>';
        }
        if(t==='numberbox'){
            var validationText = buildValidationMetadata(opt);
            return '<div style="display: flex; flex-direction: column; width: 100%;">'
                +'<input is="emby-input" type="number" class="pluginConfig emby-input"'
                +base
                +attrIf('min', opt.MinValue)
                +attrIf('max', opt.MaxValue)
                +attrIf('step', opt.Step)
                +attrIf('required', opt.Required)
                +attrIf('value', value==null?'':value)
                +' />'
                +(validationText ? '<div class="fieldDescription" style="font-size: 0.8em; color: rgba(255,255,255,0.7); margin-top: 0.3em; line-height: 1.2;">'+escapeHtml(validationText)+'</div>' : '')
                +'</div>';
        }
        var validationText = buildValidationMetadata(opt);
        return '<div style="display: flex; flex-direction: column; width: 100%;">'
            +'<input is="emby-input" type="text" class="pluginConfig emby-input"'
            +base
            +attrIf('minlength', opt.MinLength)
            +attrIf('maxlength', opt.MaxLength)
            +attrIf('pattern', sanitizePattern(opt.Pattern || opt.ValidationPattern))
            +attrIf('placeholder', opt.Placeholder)
            +attrIf('required', opt.Required)
            +attrIf('value', value==null?'':value)
            +' />'
            +(validationText ? '<div class="fieldDescription" style="font-size: 0.8em; color: rgba(255,255,255,0.7); margin-top: 0.3em; line-height: 1.2;">'+escapeHtml(validationText)+'</div>' : '')
            +'</div>';
    }

    function buildUserOptionInput(opt, value){
        if(!opt) return '';
        var t=(opt.Type||'').toLowerCase();
        var key=escapeAttr(opt.Key||'');
        var base=' data-config-control="true" data-config-key="'+key+'" data-config-type="'+escapeAttr(opt.Type||'')+'"';
        var label = opt.Name || opt.Key || '';
        var description = opt.Description || '';
        var validationText = buildValidationMetadata(opt);
        if (validationText) {
            if (description) {
                description = description + ' (' + validationText + ')';
            } else {
                description = '(' + validationText + ')';
            }
        }
        
        if(t==='checkbox'){
            return '<label class="emby-checkbox-label" style="width:auto;">'
                +'<input is="emby-checkbox" type="checkbox" class="emby-checkbox pluginConfig"'+base+(parseBoolean(value)?' checked':'')+'>'
                +'<span class="checkboxLabel">'+escapeHtml(label)+'</span>'
                +'<span class="checkboxOutline"><span class="material-icons checkboxIcon checkboxIcon-checked check"></span><span class="material-icons checkboxIcon checkboxIcon-unchecked"></span></span>'
                +'</label>'
                +(description ? '<div class="fieldDescription">'+escapeHtml(description)+'</div>' : '');
        }
        if(t==='dropdown'){
            return '<div class="selectContainer">'
                +'<label class="inputLabel inputLabelUnfocused" for="'+key+'">'+escapeHtml(label)+'</label>'
                +'<select is="emby-select" class="pluginConfig emby-select-withcolor emby-select" id="'+key+'"'+base+attrIf('required', opt.Required)+'>'+buildDropdownOptions(opt, value)+'</select>'
                +(description ? '<div class="fieldDescription">'+escapeHtml(description)+'</div>' : '')
                +'</div>';
        }
        if(t==='numberbox'){
            return '<div class="inputContainer">'
                +'<label class="inputLabel inputLabelUnfocused" for="'+key+'">'+escapeHtml(label)+'</label>'
                +'<input is="emby-input" type="number" class="pluginConfig emby-input" id="'+key+'"'
                +base
                +attrIf('min', opt.MinValue)
                +attrIf('max', opt.MaxValue)
                +attrIf('step', opt.Step)
                +attrIf('required', opt.Required)
                +attrIf('value', value==null?'':value)
                +' />'
                +(description ? '<div class="fieldDescription">'+escapeHtml(description)+'</div>' : '')
                +'</div>';
        }
        return '<div class="inputContainer">'
            +'<label class="inputLabel inputLabelUnfocused" for="'+key+'">'+escapeHtml(label)+'</label>'
            +'<input is="emby-input" type="text" class="pluginConfig emby-input" id="'+key+'"'
            +base
            +attrIf('minlength', opt.MinLength)
            +attrIf('maxlength', opt.MaxLength)
            +attrIf('pattern', sanitizePattern(opt.Pattern || opt.ValidationPattern))
            +attrIf('placeholder', opt.Placeholder)
            +attrIf('required', opt.Required)
            +attrIf('value', value==null?'':value)
            +' />'
            +(description ? '<div class="fieldDescription">'+escapeHtml(description)+'</div>' : '')
            +'</div>';
    }

    function collectOptionValues(container){
        var list=[];
        if(!container) return list;
        container.querySelectorAll('.pluginConfig[data-config-key]').forEach(function(el){
            var key=el.getAttribute('data-config-key');
            var t=(el.getAttribute('data-config-type')||'').toLowerCase();
            var raw;
            if(t==='checkbox') raw = !!el.checked; else raw = el.value;
            var mappedType = (t==='checkbox') ? 'bool' : (t==='numberbox') ? 'double' : 'string';
            list.push({ Key:key, Value: raw==null ? '' : String(raw), Type: mappedType });
        });
        return list;
    }
    
    function buildValidationMetadata(opt){
        if(!opt) return '';
        var t = (opt.Type || '').toLowerCase();
        var validationInfo = [];
        
        if (opt.Required) {
            validationInfo.push('Required');
        }
        
        if (t === 'textbox') {
            if (opt.MinLength !== undefined && opt.MinLength !== null) {
                validationInfo.push('Min: ' + opt.MinLength);
            }
            if (opt.MaxLength !== undefined && opt.MaxLength !== null) {
                validationInfo.push('Max: ' + opt.MaxLength);
            }
            if (opt.Pattern || opt.ValidationPattern) {
                var pattern = opt.Pattern || opt.ValidationPattern;
                validationInfo.push('Pattern: ' + pattern);
            }
        } else if (t === 'numberbox') {
            if (opt.MinValue !== undefined && opt.MinValue !== null) {
                validationInfo.push('Min: ' + opt.MinValue);
            }
            if (opt.MaxValue !== undefined && opt.MaxValue !== null) {
                validationInfo.push('Max: ' + opt.MaxValue);
            }
            if (opt.Step !== undefined && opt.Step !== null) {
                validationInfo.push('Step: ' + opt.Step);
            }
        }
        
        return validationInfo.length > 0 ? validationInfo.join(', ') : '';
    }

    function convertDropdownKeyToLabel(opt, key){
        if (!opt || !key || (opt.Type || '').toLowerCase() !== 'dropdown') return key;
        if (!opt.DropdownOptions || !opt.DropdownLabels) return key;
        
        var keyIndex = opt.DropdownOptions.indexOf(String(key));
        if (keyIndex !== -1 && opt.DropdownLabels[keyIndex]) {
            return opt.DropdownLabels[keyIndex];
        }
        return key;
    }

    function validateAndCollectInputs(container) {
        const validation = validateInputs(container);
        if (!validation.valid) {
            highlightInvalidInputs(container);
            return { valid: false, message: validation.message };
        }
        return { valid: true, collected: collectOptionValues(container) };
    }

    function highlightInvalidInputs(container) {
        const invalidInputs = container.querySelectorAll(':invalid, .invalid');
        invalidInputs.forEach(function(input) {
            input.classList.add('invalid');
            input.style.borderColor = 'var(--theme-error-text, #f44336)';
        });
    }

    function buildSectionInfo(sectionInfo, options) {
        if (!sectionInfo || !sectionInfo.Info) return '';
        
        options = options || {};
        var isAdmin = options.isAdmin || false;
        var wrapperClass = options.wrapperClass || '';
        var wrapperStyle = options.wrapperStyle || 'margin-bottom: 1em;';
        
        var infoHtml = '';
        if (wrapperClass || wrapperStyle) {
            infoHtml += '<div' + (wrapperClass ? ' class="' + wrapperClass + '"' : '') + (wrapperStyle ? ' style="' + wrapperStyle + '"' : '') + '>';
        }
        
        var contentParts = [];
        
        if (sectionInfo.Info.description) {
            contentParts.push('<span style="color: var(--theme-text-color); font-size: 1em; line-height: 1.4;">' + escapeHtml(sectionInfo.Info.description) + '</span>');
        }
        
        if (isAdmin && sectionInfo.Info.adminNotes) {
            contentParts.push('<span style="color: var(--theme-accent-color, #ffa726); font-size: 0.95em; font-style: italic;">' + escapeHtml(sectionInfo.Info.adminNotes) + '</span>');
        }
        
        var allLinksHtml = '';
        var hasAnyLinks = false;
        
        if (sectionInfo.Info.versionControl) {
            var vcs = sectionInfo.Info.versionControl;
            
            if (vcs.repositoryUrl) {
                hasAnyLinks = true;
                allLinksHtml += '<a href="' + escapeAttr(vcs.repositoryUrl) + '" target="_blank" rel="noopener" style="color: var(--theme-primary-color, #00a4dc); text-decoration: none; font-size: 0.9em; display: inline-flex; align-items: center; gap: 0.3em; padding: 0.15em 0.4em; border-radius: 3px; transition: all 0.2s ease; border: 1px solid rgba(255,255,255,0.1); background: rgba(255,255,255,0.02);" onmouseover="this.style.background=\'rgba(255,255,255,0.05)\'; this.style.borderColor=\'rgba(255,255,255,0.2)\';" onmouseout="this.style.background=\'rgba(255,255,255,0.02)\'; this.style.borderColor=\'rgba(255,255,255,0.1)\';">';
                allLinksHtml += '<svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><path d="M12,2A10,10 0 0,0 2,12C2,16.42 4.87,20.17 8.84,21.5C9.34,21.58 9.5,21.27 9.5,21C9.5,20.77 9.5,20.14 9.5,19.31C6.73,19.91 6.14,17.97 6.14,17.97C5.68,16.81 5.03,16.5 5.03,16.5C4.12,15.88 5.1,15.9 5.1,15.9C6.1,15.97 6.63,16.93 6.63,16.93C7.5,18.45 8.97,18 9.54,17.76C9.63,17.11 9.89,16.67 10.17,16.42C7.95,16.17 5.62,15.31 5.62,11.5C5.62,10.39 6,9.5 6.65,8.79C6.55,8.54 6.2,7.5 6.75,6.15C6.75,6.15 7.59,5.88 9.5,7.17C10.29,6.95 11.15,6.84 12,6.84C12.85,6.84 13.71,6.95 14.5,7.17C16.41,5.88 17.25,6.15 17.25,6.15C17.8,7.5 17.45,8.54 17.35,8.79C18,9.5 18.38,10.39 18.38,11.5C18.38,15.32 16.04,16.16 13.81,16.41C14.17,16.72 14.5,17.33 14.5,18.26C14.5,19.6 14.5,20.68 14.5,21C14.5,21.27 14.66,21.59 15.17,21.5C19.14,20.16 22,16.42 22,12A10,10 0 0,0 12,2Z"></path></svg>';
                allLinksHtml += 'Source</a>';
            }
            
            if (vcs.issuesUrl) {
                if (hasAnyLinks) {
                    allLinksHtml += ' ';
                }
                hasAnyLinks = true;
                allLinksHtml += '<a href="' + escapeAttr(vcs.issuesUrl) + '" target="_blank" rel="noopener" style="color: var(--theme-accent-text-color, #1e88e5); text-decoration: none; font-size: 0.9em; display: inline-flex; align-items: center; gap: 0.3em; padding: 0.15em 0.4em; border-radius: 3px; transition: all 0.2s ease; border: 1px solid rgba(255,255,255,0.1); background: rgba(255,255,255,0.02);" onmouseover="this.style.background=\'rgba(255,255,255,0.05)\'; this.style.borderColor=\'rgba(255,255,255,0.2)\';" onmouseout="this.style.background=\'rgba(255,255,255,0.02)\'; this.style.borderColor=\'rgba(255,255,255,0.1)\';">';
                allLinksHtml += '<svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><path d="M12,2C6.48,2 2,6.48 2,12C2,17.52 6.48,22 12,22C17.52,22 22,17.52 22,12C22,6.48 17.52,2 12,2M12,17C11.45,17 11,16.55 11,16C11,15.45 11.45,15 12,15C12.55,15 13,15.45 13,16C13,16.55 12.55,17 12,17M12,7C12.55,7 13,7.45 13,8V12C13,12.55 12.55,13 12,13C11.45,13 11,12.55 11,12V8C11,7.45 11.45,7 12,7Z"></path></svg>';
                allLinksHtml += 'Issues</a>';
            }
        }
        
        if (sectionInfo.Info.featureRequestUrl) {
            if (hasAnyLinks) {
                allLinksHtml += ' ';
            }
            hasAnyLinks = true;
            allLinksHtml += '<a href="' + escapeAttr(sectionInfo.Info.featureRequestUrl) + '" target="_blank" rel="noopener" style="color: var(--theme-primary-color, #00a4dc); text-decoration: none; font-size: 0.9em; display: inline-flex; align-items: center; gap: 0.3em; padding: 0.15em 0.4em; border-radius: 3px; transition: all 0.2s ease; border: 1px solid rgba(255,255,255,0.1); background: rgba(255,255,255,0.02);" onmouseover="this.style.background=\'rgba(255,255,255,0.05)\'; this.style.borderColor=\'rgba(255,255,255,0.2)\';" onmouseout="this.style.background=\'rgba(255,255,255,0.02)\'; this.style.borderColor=\'rgba(255,255,255,0.1)\';">';
            allLinksHtml += '<svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><path d="M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z"></path></svg>';
            allLinksHtml += 'Request</a>';
        }
        
        if (hasAnyLinks) {
            contentParts.push('<span style="display: inline-flex; gap: 0.4em; flex-wrap: wrap; align-items: center;">' + allLinksHtml + '</span>');
        }
        
        if (contentParts.length > 0) {
            infoHtml += '<div style="font-size: 1em; line-height: 1.4; margin-bottom: 1em; padding: 0.6em 0.8em; background: rgba(255,255,255,0.02); border-radius: 4px;">';
            infoHtml += contentParts.join('<br style="margin: 0.2em 0;">');
            infoHtml += '</div>';
        }
        
        if (wrapperClass || wrapperStyle) {
            infoHtml += '</div>';
        }
        
        return infoHtml;
    }

    if(!document.getElementById('hss-shared-validation-css')){
        try {
            var st=document.createElement('style');
            st.id='hss-shared-validation-css';
            st.textContent='.pluginConfig.invalid,.pluginConfig:invalid{border-color:var(--theme-error-text,#f44336)!important;}';
            document.head.appendChild(st);
        } catch(_){}
    }

    window.HSSShared = Object.assign(window.HSSShared||{}, {
        parseBoolean: parseBoolean,
        escapeHtml: escapeHtml,
        escapeAttr: escapeAttr,
        
        validateInputs: validateInputs,
        validateAndCollectInputs: validateAndCollectInputs,
        highlightInvalidInputs: highlightInvalidInputs,
        attachUnifiedValidation: attachUnifiedValidation,
        
        buildDropdownOptions: buildDropdownOptions,
        buildAdminOptionInput: buildAdminOptionInput,
        buildUserOptionInput: buildUserOptionInput,
        collectOptionValues: collectOptionValues,
        
        buildSectionInfo: buildSectionInfo,
        
        buildValidationMetadata: buildValidationMetadata,
        convertDropdownKeyToLabel: convertDropdownKeyToLabel,
    });
})();
