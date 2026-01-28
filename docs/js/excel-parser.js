/**
 * Excel Parser Module
 * Extracts diagnostics JSON from first column of Excel files
 */

class ExcelParser {
    /**
     * Check if file is an Excel file
     * @param {string} filename - File name
     * @returns {boolean}
     */
    static isExcelFile(filename) {
        const ext = filename.toLowerCase().split('.').pop();
        return ['xlsx', 'xls', 'xlsb', 'ods', 'csv'].includes(ext);
    }

    /**
     * Parse Excel file and extract first column as text lines
     * @param {ArrayBuffer} arrayBuffer - File content as ArrayBuffer
     * @param {function} progressCallback - Progress callback (message, percent)
     * @returns {string} Content with one JSON per line
     */
    parse(arrayBuffer, progressCallback = null) {
        if (progressCallback) {
            progressCallback('Reading Excel file...', 5);
        }

        // Parse workbook
        const workbook = XLSX.read(arrayBuffer, { type: 'array' });
        
        if (progressCallback) {
            progressCallback('Extracting data from first sheet...', 15);
        }

        // Get first sheet
        const firstSheetName = workbook.SheetNames[0];
        const sheet = workbook.Sheets[firstSheetName];
        
        // Convert to array of arrays (raw data)
        const data = XLSX.utils.sheet_to_json(sheet, { header: 1, defval: '' });
        
        if (progressCallback) {
            progressCallback(`Processing ${data.length} rows...`, 25);
        }

        // Extract first column, filter empty rows
        const lines = [];
        let skippedHeader = false;

        for (let i = 0; i < data.length; i++) {
            const row = data[i];
            const cell = row[0];
            
            // Skip empty cells
            if (!cell || (typeof cell === 'string' && !cell.trim())) {
                continue;
            }

            const cellStr = String(cell).trim();
            
            // Skip header row (if first non-empty cell doesn't look like JSON)
            if (!skippedHeader && !cellStr.startsWith('{')) {
                skippedHeader = true;
                continue;
            }

            // Only include cells that look like JSON objects
            if (cellStr.startsWith('{')) {
                lines.push(cellStr);
            }
        }

        if (progressCallback) {
            progressCallback(`Found ${lines.length} diagnostics entries`, 30);
        }

        return lines.join('\n');
    }

    /**
     * Get sheet info for display
     * @param {ArrayBuffer} arrayBuffer - File content
     * @returns {Object} Sheet info
     */
    getSheetInfo(arrayBuffer) {
        const workbook = XLSX.read(arrayBuffer, { type: 'array' });
        const firstSheetName = workbook.SheetNames[0];
        const sheet = workbook.Sheets[firstSheetName];
        const range = XLSX.utils.decode_range(sheet['!ref'] || 'A1');
        
        return {
            sheetCount: workbook.SheetNames.length,
            sheetName: firstSheetName,
            rowCount: range.e.r - range.s.r + 1,
            colCount: range.e.c - range.s.c + 1
        };
    }
}
