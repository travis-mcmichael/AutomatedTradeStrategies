// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

const Defaults = {
    grid: {
        defaultGridHeight: '500px',
        defaultPageSize: 5,
        layout: 'fitColumns',
        editColumnWidth: 60,
        editColumnElement: `<i class='bi bi-pencil'></i>`,
        buildGrid: (sel, c, url) => {
            return new Tabulator(sel, {
                layout: Defaults.grid.layout,
                height: Defaults.grid.defaultGridHeight,
                ajaxConfig: 'GET',
                ajaxURL: url,
                pagination: true,
                paginationSize: Defaults.grid.defaultPageSize,
                columns: c
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
    },
};