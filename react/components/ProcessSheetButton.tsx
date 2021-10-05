/* eslint-disable @typescript-eslint/no-explicit-any */
import type { FC } from 'react'
import React, { useState } from 'react'
import { useMutation } from 'react-apollo'
import { FormattedMessage } from 'react-intl'
import { Alert, Button, Card, Divider } from 'vtex.styleguide'

import M_PROCESS_SHEET from '../mutations/ProcessSheet.gql'

const ProcessSheetButton: FC = () => {
  const [showAlert, setShowAlert] = useState(true)
  const [sheetImport, { loading: sheetProcessing, data: sheetProcessed }] =
    useMutation(M_PROCESS_SHEET)

  const hasErrors = sheetProcessed?.processSheet.slice(-1) !== '0'
  const displayAlert = !sheetProcessing && sheetProcessed && showAlert

  return (
    <div>
      {displayAlert && (
        <div className="mb2">
          <Alert
            type={hasErrors ? 'warning' : 'success'}
            onClose={() => setShowAlert(false)}
          >
            {hasErrors ? (
              <FormattedMessage
                id="admin/sheets-catalog-import.sheet-import.error"
                values={{ result: sheetProcessed?.processSheet }}
              />
            ) : (
              sheetProcessed.processSheet
            )}
          </Alert>
        </div>
      )}
      <Card>
        <div className="flex">
          <div className="w-70">
            <p>
              <FormattedMessage id="admin/sheets-catalog-import.sheet-import.description" />
            </p>
          </div>
          <div
            style={{ flexGrow: 1 }}
            className="flex items-stretch w-20 justify-center"
          >
            <Divider orientation="vertical" />
          </div>
          <div className="w-30 items-center flex">
            <Button
              variation="secondary"
              collapseLeft
              block
              isLoading={sheetProcessing}
              onClick={() => {
                sheetImport()
                setShowAlert(true)
              }}
            >
              <FormattedMessage id="admin/sheets-catalog-import.sheet-import.button" />
            </Button>
          </div>
        </div>
      </Card>
      <br />
    </div>
  )
}

export default ProcessSheetButton
