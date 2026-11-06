// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

const Defaults = {
    grid: {
        defaultGridHeight: '500px',
        defaultPageSize: 5,
        layout: 'fitColumns',
        editColumnWidth: 60,
        editColumnElement: `<i class='bi bi-pencil'></i>`,
        // options are merged over the defaults (e.g. { pagination: false }).
        buildGrid: (sel, c, url, options = {}) => {
            return new Tabulator(sel, {
                layout: Defaults.grid.layout,
                height: Defaults.grid.defaultGridHeight,
                ajaxConfig: 'GET',
                ajaxURL: url,
                pagination: true,
                paginationSize: Defaults.grid.defaultPageSize,
                columns: c,
                ...options
            });
        },
        // dest is either a URL prefix (navigates to dest/{value}) or a function called with the row's data.
        buildEditColumn: (f, dest) => {
            return {
                title: '',
                field: f,
                hozAlign: 'center',
                width: Defaults.grid.editColumnWidth,
                headerSort: false,
                formatter: () => `<button type='button' class='btn btn-sm btn-outline-primary' title='Edit'>${Defaults.grid.editColumnElement}</button>`,
                cellClick: (e, cell) => {
                    if (typeof dest === 'function') {
                        dest(cell.getRow().getData());
                        return;
                    }
                    const id = cell.getValue();
                    window.location.href = `${dest}/${id}`;        
                }
            };
        },
        expanderIcon: (expanded) => `<i class='bi bi-chevron-${expanded ? 'down' : 'right'}'></i>`,
        expanderColumn: (hasChildren, isExpanded, toggle) => ({
            title: '',
            hozAlign: 'center',
            width: Defaults.grid.editColumnWidth,
            headerSort: false,
            formatter: (cell) => hasChildren(cell.getRow().getData()) ? Defaults.grid.expanderIcon(isExpanded(cell.getRow().getData())) : '',
            cellClick: (e, cell) => {
                const row = cell.getRow();
                if (!hasChildren(row.getData())) return;
                toggle(row.getData());
                row.reformat();
            }
        }),
        percentFormatter: (cell) => `${(cell.getValue() * 100).toFixed(1)}%`,
        toggleExpanded: (set, id) => {
            if (set.has(id)) set.delete(id); 
            else set.add(id);
        },
    },
    // Escapes text for use inside HTML element content or attribute values.
    escapeHtml: (v) => String(v).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])),
};